import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

import '../lib/domain/encoding.dart';
import '../lib/domain/media.dart';
import '../lib/domain/output_plan.dart';
import '../lib/domain/task.dart';
import '../lib/platform/encoders.dart';
import '../lib/platform/host.dart';
import '../lib/services/binaries.dart';
import '../lib/services/command_builder.dart';
import '../lib/services/media_processor.dart';
import '../lib/services/process_runner.dart';
import '../lib/services/probe.dart';
import '../lib/services/waveform.dart';
import '../lib/core/log.dart';
import 'helpers/fakes.dart';
import 'helpers/metrics.dart';

/// Executes the generated ffmpeg commands against a real toolchain. Runs by
/// default whenever the bundled binaries are present.
void main() {
  test('real ffmpeg matrix: containers, copy, encode, preview, PNG, HDR',
      () async {
    final binDir = p.join(Directory.current.path, 'data',
        'ffmpeg-release-full-shared', 'bin');
    final suffix = currentHostOs() == HostOs.windows ? '.exe' : '';
    final ffmpeg = p.join(binDir, 'ffmpeg$suffix');
    final ffprobe = p.join(binDir, 'ffprobe$suffix');
    if (!File(ffmpeg).existsSync() || !File(ffprobe).existsSync()) {
      print('[METRIC] real_matrix skipped (binaries absent)');
      return;
    }

    final temp = Directory.systemTemp.createTempSync('mp_real_matrix_');
    final host = detectHostCapabilities();
    final logs = LogStore(scheduler: const SystemFlushScheduler());
    final group = ProcessGroup();
    final runner = SystemProcessRunner(group: group);
    final binaries = ExeRelativeBinaryLocator(
      executableDirectory: temp.path,
      os: currentHostOs(),
      overrideDirectory: binDir,
    );
    final probe = MediaProbeService(runner: runner, binaries: binaries);
    final waveforms = WaveformService(
      runner: runner,
      binaries: binaries,
      logs: logs,
      cacheDirectory: p.join(temp.path, 'waveforms'),
    );
    final stopwatch = Stopwatch()..start();
    var bytesWritten = 0;
    var tasks = 0;
    final containers = <String>{};

    try {
      final input = p.join(temp.path, 'source.mp4');
      final generation = await Process.run(ffmpeg, [
        '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', 'testsrc=s=160x90:r=25:d=0.4',
        '-f', 'lavfi', '-i', 'sine=frequency=440:duration=0.4',
        '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p',
        '-c:a', 'aac', '-shortest', '-y', input,
      ]);
      expect(generation.exitCode, 0, reason: generation.stderr.toString());

      final matrix = <(ContainerFormat, QualityPreset, bool)>[
        (ContainerFormat.mp4, QualityPreset.speed, true),
        (ContainerFormat.mp4, QualityPreset.quality, false),
        (ContainerFormat.webm, QualityPreset.speed, false),
        (ContainerFormat.mkv, QualityPreset.balanced, true),
      ];

      for (final (container, quality, allowCopy) in matrix) {
        final processor = MediaProcessor(
          runner: runner,
          binaries: binaries,
          host: host,
          logs: logs,
          probe: probe,
          config: MediaProcessorConfig(
            enableHardware: false,
            enableStreamCopy: allowCopy,
            processTimeout: const Duration(minutes: 2),
          ),
        );
        final result = await processor.execute(TaskRequest(
          kind: TaskKind.full,
          inputPath: input,
          container: container,
          quality: quality,
          overwrite: true,
        ));
        expect(result.status, TaskStatus.completed,
            reason: '${container.name}/$quality: '
                '${result.failure?.message ?? logs.exportText()}');
        expect(result.outputs.length, 2);
        expect(result.stages.every((s) => s.command.isNotEmpty), isTrue,
            reason: 'every stage must carry its exact command');
        for (final output in result.outputs) {
          expect(File(output.path).existsSync(), isTrue);
          final check = await Process.run(ffprobe, [
            '-v', 'error', '-show_entries', 'format=duration',
            '-of', 'json', output.path,
          ]);
          expect(check.exitCode, 0,
              reason: 'invalid ${output.path}: ${check.stderr}');
          bytesWritten += output.bytes;
        }
        containers.add(container.name);
        tasks++;
        await processor.dispose();
      }

      // The explicit resolution policy must be honoured by a real encoder,
      // not only by the command builder: 160x90 source -> 1280x720 output.
      final upscaleProcessor = MediaProcessor(
        runner: runner,
        binaries: binaries,
        host: host,
        logs: logs,
        probe: probe,
        config: const MediaProcessorConfig(
          enableHardware: false,
          enableStreamCopy: false,
        ),
      );
      final upscaled = await upscaleProcessor.execute(TaskRequest(
        kind: TaskKind.full,
        inputPath: input,
        container: ContainerFormat.mp4,
        quality: QualityPreset.speed,
        outputResolution: OutputResolution.p720,
        overwrite: true,
      ));
      expect(upscaled.status, TaskStatus.completed,
          reason: upscaled.failure?.message ?? logs.exportText());
      final videoOutput = upscaled.outputs
          .firstWhere((output) => output.kind == OutputKind.video);
      final dimensions = await Process.run(ffprobe, [
        '-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=width,height',
        '-of', 'csv=p=0', videoOutput.path,
      ]);
      expect(dimensions.exitCode, 0);
      expect(dimensions.stdout.toString().trim(), '1280,720',
          reason: 'real encode must reach the selected 720p target');
      bytesWritten += videoOutput.bytes;
      tasks++;
      await upscaleProcessor.dispose();

      // Clip preview with exact seek.
      final previewProcessor = MediaProcessor(
        runner: runner,
        binaries: binaries,
        host: host,
        logs: logs,
        probe: probe,
        config: const MediaProcessorConfig(
          enableHardware: false,
          enableStreamCopy: false,
        ),
      );
      final preview = await previewProcessor.execute(TaskRequest(
        kind: TaskKind.preview,
        inputPath: input,
        container: ContainerFormat.mp4,
        quality: QualityPreset.speed,
        range: const ClipRange(startMs: 0, endMs: 200),
        overwrite: true,
      ));
      expect(preview.status, TaskStatus.completed,
          reason: preview.failure?.message ?? logs.exportText());
      tasks++;
      await previewProcessor.dispose();

      // Waveform must really be PNG, not MJPEG hidden behind a .png name.
      final waveform = await waveforms.ensure(input, width: 128, height: 32);
      expect(waveform.isOk, isTrue);
      final header = File(waveform.valueOrNull!).readAsBytesSync().take(4);
      expect(header, [0x89, 0x50, 0x4E, 0x47],
          reason: 'waveform output must be a PNG');

      // Execute the HDR filter chain generated by the builder on 10-bit input.
      final media = sampleMedia(height: 1080, hdr: true);
      final spec = CommandSpec(
        input: input,
        output: p.join(temp.path, 'hdr.mp4.part'),
        media: media,
        profile: ProfileRegistry.of(ContainerFormat.mp4),
        quality: QualityPreset.balanced,
        targetResolution: media.resolution,
        kind: OutputKind.video,
        threads: host.planFor(GpuVendor.software),
        encoder: EncoderCatalog.all
            .firstWhere((e) => e.id == 'libx264'),
      );
      final hdrFilter = FfmpegCommandBuilder.planVideo(spec).filter!;
      final hdr = await runner.run(ProcessRequest(
        label: 'hdr-chain',
        executable: ffmpeg,
        arguments: [
          '-hide_banner', '-loglevel', 'error',
          '-f', 'lavfi', '-i',
          'testsrc=s=64x64:r=1:d=0.1,format=yuv420p10le',
          '-vf', hdrFilter,
          '-frames:v', '1', '-f', 'null', '-',
        ],
        timeout: const Duration(seconds: 30),
      ));
      expect(hdr.succeeded, isTrue,
          reason: 'HDR chain must execute: ${hdr.describe()}');

      stopwatch.stop();
      recordMetric('real_matrix', {
        'containers': containers.length,
        'tasks': tasks,
        'bytes': bytesWritten,
        'png_magic': true,
        'hdr_chain_ok': hdr.succeeded,
        'duration_ms': stopwatch.elapsedMilliseconds,
        'peak_processes': runner.peakConcurrent,
        'total_processes': runner.totalProcesses,
        'queued_processes': runner.queuedProcesses,
      });
    } finally {
      await logs.dispose();
      temp.deleteSync(recursive: true);
    }
  });
}
