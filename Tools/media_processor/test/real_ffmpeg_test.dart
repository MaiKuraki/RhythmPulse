import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

import '../lib/core/log.dart';
import '../lib/core/outcome.dart';
import '../lib/domain/encoding.dart';
import '../lib/domain/media.dart';
import '../lib/domain/task.dart';
import '../lib/platform/host.dart';
import '../lib/services/binaries.dart';
import '../lib/services/media_processor.dart';
import '../lib/services/process_runner.dart';
import '../lib/services/probe.dart';
import '../lib/services/waveform.dart';
import 'helpers/metrics.dart';

/// Integration test against the bundled ffmpeg binaries. Runs by default
/// whenever the binaries are present; skips only on a source-only checkout.
void _mark(String stage) => print('[REAL] $stage ${DateTime.now()}');

void main() {
  test('real ffmpeg: synth -> probe -> encode -> clip -> waveform -> timeout',
      () async {
    final binDir = p.join(Directory.current.path, 'data',
        'ffmpeg-release-full-shared', 'bin');
    final suffix = currentHostOs() == HostOs.windows ? '.exe' : '';
    final ffmpeg = p.join(binDir, 'ffmpeg$suffix');
    final ffprobe = p.join(binDir, 'ffprobe$suffix');
    if (!File(ffmpeg).existsSync() || !File(ffprobe).existsSync()) {
      print('[METRIC] real_ffmpeg skipped (binaries not present)');
      return;
    }

    final temp = Directory.systemTemp.createTempSync('mp_real_ffmpeg_');
    final host = detectHostCapabilities();
    final logs = LogStore(scheduler: const SystemFlushScheduler());
    final group = ProcessGroup();
    final runner = SystemProcessRunner(group: group);
    // Binary naming follows the physical OS, while capabilities may be
    // overridden to simulate other hardware scenarios.
    final binaries = ExeRelativeBinaryLocator(
      executableDirectory: temp.path,
      os: currentHostOs(),
      overrideDirectory: binDir,
    );
    MediaProcessor? processor;
    var processes = 0;
    try {
      final input = p.join(temp.path, 'source.mp4');
      final generation = await Process.run(ffmpeg, [
        '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', 'testsrc=size=320x180:rate=25:duration=1',
        '-f', 'lavfi', '-i', 'sine=frequency=440:duration=1',
        '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p',
        '-c:a', 'aac', '-shortest', '-y', input,
      ]);
      expect(generation.exitCode, 0, reason: generation.stderr.toString());
      expect(File(input).existsSync(), isTrue);
      _mark('synthetic media generated');

      final verified = await binaries.verify(runner);
      expect(verified.isOk, isTrue,
          reason: 'bundled ffmpeg/ffprobe must be executable');

      final probe = MediaProbeService(runner: runner, binaries: binaries);
      final inspected = await probe.inspect(input);
      expect(inspected.isOk, isTrue,
          reason: inspected.failureOrNull?.message ?? '');
      expect(inspected.valueOrNull!.hasVideo, isTrue);
      expect(inspected.valueOrNull!.hasAudio, isTrue);
      expect(inspected.valueOrNull!.durationSeconds, greaterThan(0));
      expect(probe.cacheStats.hits, 0);
      await probe.inspect(input);
      expect(probe.cacheStats.hits, 1);
      _mark('binaries + probe verified');

      processor = MediaProcessor(
        runner: runner,
        binaries: binaries,
        host: host,
        logs: logs,
        probe: probe,
        group: group,
        config: const MediaProcessorConfig(
          enableHardware: false,
          enableStreamCopy: false,
          processTimeout: Duration(minutes: 2),
        ),
      );
      final progress = <double>[];
      final full = await processor.execute(
        TaskRequest(
          kind: TaskKind.full,
          inputPath: input,
          container: ContainerFormat.mp4,
          quality: QualityPreset.speed,
          overwrite: true,
        ),
        onProgress: (value) => progress.add(value.fraction),
      );
      expect(full.status, TaskStatus.completed,
          reason: full.failure?.message ?? logs.exportText());
      expect(full.outputs.length, 2);
      for (final output in full.outputs) {
        expect(File(output.path).existsSync(), isTrue);
        expect(output.bytes, greaterThan(0));
        final check = await Process.run(ffprobe, [
          '-v', 'error', '-show_entries', 'format=duration', '-of', 'json',
          output.path,
        ]);
        expect(check.exitCode, 0,
            reason: 'invalid output ${output.path}: ${check.stderr}');
      }
      for (var i = 1; i < progress.length; i++) {
        expect(progress[i], greaterThanOrEqualTo(progress[i - 1]));
      }
      _mark('full encode completed');

      final clip = await processor.execute(
        TaskRequest(
          kind: TaskKind.preview,
          inputPath: input,
          container: ContainerFormat.mp4,
          quality: QualityPreset.speed,
          range: const ClipRange(startMs: 0, endMs: 500),
          overwrite: true,
        ),
      );
      expect(clip.status, TaskStatus.completed,
          reason: clip.failure?.message ?? logs.exportText());
      _mark('clip completed');

      final waveforms = WaveformService(
        runner: runner,
        binaries: binaries,
        logs: logs,
        cacheDirectory: p.join(temp.path, 'waveforms'),
      );
      final waveform = await waveforms.ensure(input, width: 256, height: 64);
      expect(waveform.isOk, isTrue);
      expect(File(waveform.valueOrNull!).existsSync(), isTrue);
      final second = await waveforms.ensure(input, width: 256, height: 64);
      expect(second.valueOrNull, waveform.valueOrNull);
      expect(waveforms.cacheStats.hits, 1);
      _mark('waveform completed');

      final timeoutProcessor = MediaProcessor(
        runner: runner,
        binaries: binaries,
        host: host,
        logs: logs,
        probe: probe,
        config: const MediaProcessorConfig(
          enableHardware: false,
          enableStreamCopy: false,
          processTimeout: Duration(milliseconds: 20),
        ),
      );
      final timedOut = await timeoutProcessor.execute(
        TaskRequest(
          kind: TaskKind.full,
          inputPath: input,
          container: ContainerFormat.mp4,
          quality: QualityPreset.balanced,
          overwrite: true,
        ),
      );
      expect(timedOut.status, TaskStatus.failed);
      expect(timedOut.failure?.kind, FailureKind.timeout);
      expect(
          Directory(temp.path)
              .listSync()
              .where((e) => e.path.endsWith('.part')),
          isEmpty,
          reason: 'no half-written files may survive a timeout');

      processes = processor.metrics.snapshot()['stages']! +
          timeoutProcessor.metrics.snapshot()['stages']!;
      recordMetric('real_ffmpeg', {
        'host': host.toString(),
        'outputs': full.outputs.length,
        'bytes': processor.metrics.snapshot()['bytesWritten'],
        'stages': processes,
        'progress_samples': progress.length,
        'probe_hit_rate':
            double.parse(probe.cacheStats.hitRate.toStringAsFixed(4)),
      });
      await timeoutProcessor.dispose();
    } finally {
      await processor?.dispose();
      await logs.dispose();
      temp.deleteSync(recursive: true);
    }
  });
}
