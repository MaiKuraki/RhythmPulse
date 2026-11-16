import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import '../lib/core/cancellation.dart';
import '../lib/core/log.dart';
import '../lib/core/outcome.dart';
import '../lib/domain/encoding.dart';
import '../lib/domain/media.dart';
import '../lib/domain/task.dart';
import '../lib/platform/host.dart';
import '../lib/services/atomic_output.dart';
import '../lib/services/encoder_selector.dart';
import '../lib/services/media_processor.dart';
import '../lib/services/probe.dart';
import 'helpers/fakes.dart';
import 'helpers/rig.dart';

void main() {
  group('MediaProbeService.parse', () {
    test('accepts well-formed and incomplete but valid ffprobe JSON', () {
      final ok = MediaProbeService.parse(sampleProbeJson(),
          path: 'a.mp4', sizeBytes: 10, modifiedMs: 1);
      expect(ok.isOk, isTrue);
      final info = ok.valueOrNull!;
      expect(info.hasVideo, isTrue);
      expect(info.hasAudio, isTrue);
      expect(info.durationSeconds, 10);
      expect(info.resolution.height, 1080);
      expect(MediaProbeService.parse(sampleProbeJson(hdr: true),
              path: 'a.mp4', sizeBytes: 10, modifiedMs: 1)
          .valueOrNull!
          .hdr,
          isTrue);
      expect(MediaProbeService.parse(sampleProbeJson(video: false),
              path: 'a.mp3', sizeBytes: 10, modifiedMs: 1)
          .valueOrNull!
          .type,
          MediaType.audio);
    });

    test('never throws on malformed, hostile or extreme input', () {
      for (final payload in [
        '', 'not json', '[]', '{}', '{"streams":{}}',
        '{"streams":[]}', '{"streams":[null,3,"x"]}',
        '{"streams":[{"codec_type":"video","width":-4}]}',
        '{"streams":[{"codec_type":"video"}],"format":{"duration":"NaN"}}',
        '{"streams":[{"codec_type":"audio"}],"format":{"duration":1e309}}',
        '{"streams":[{"codec_type":"video","bit_rate":"999999999999999999999"}]}',
        '\u0000\ufffd',
      ]) {
        final outcome = MediaProbeService.parse(payload,
            path: 'x', sizeBytes: 0, modifiedMs: 0);
        // Either a well-defined result or a structured error; never an exception.
        expect(outcome.isOk || outcome.isErr, isTrue);
      }
      final noStreams = MediaProbeService.parse('{"streams":[]}',
          path: 'x', sizeBytes: 0, modifiedMs: 0);
      expect(noStreams.failureOrNull?.kind, FailureKind.unsupported);
    });
  });

  group('Caches', () {
    test('probe results are cached by content fingerprint', () async {
      final rig = ProcessorRig();
      final first = await rig.probe.inspect(rig.input);
      final second = await rig.probe.inspect(rig.input);
      expect(first.isOk, isTrue);
      expect(second.valueOrNull!.fingerprint, first.valueOrNull!.fingerprint);
      expect(rig.runner.countFor('ffprobe'), 1);
      expect(rig.probe.cacheStats.hits, 1);
      rig.probe.invalidateAll();
      await rig.probe.inspect(rig.input);
      expect(rig.runner.countFor('ffprobe'), 2);
      await rig.dispose();
    });

    test('encoder enumeration is cached until invalidated', () async {
      final rig = ProcessorRig();
      final host = const HostCapabilities(
          os: HostOs.windows,
          architecture: CpuArchitecture.x64,
          logicalCores: 8);
      final first = await rig.encoders.plan(VideoCodec.h264, host);
      final second = await rig.encoders.plan(VideoCodec.h264, host);
      expect(first.isOk, isTrue);
      expect(second.isOk, isTrue);
      expect(rig.runner.countFor('encoder-list'), 1);
      rig.encoders.invalidate();
      await rig.encoders.plan(VideoCodec.h264, host);
      expect(rig.runner.countFor('encoder-list'), 2);
      await rig.dispose();
    });

    test('hardware candidates are ordered with software as final fallback',
        () async {
      final runner = FakeProcessRunner(byLabel: {
        'encoder-list': const ScriptedOutcome(stdoutLines: [
          ' V....D h264_nvenc NVIDIA',
          ' V....D h264_qsv Intel',
          ' V....D h264_amf AMD',
          ' V....D libx264 x264',
        ]),
      });
      final logs = LogStore(scheduler: ManualScheduler());
      final binaries = FakeBinaryLocator();
      final selector = EncoderSelector(
          runner: runner, binaries: binaries, logs: logs);
      final plan = await selector.plan(
        VideoCodec.h264,
        const HostCapabilities(
            os: HostOs.windows,
            architecture: CpuArchitecture.x64,
            logicalCores: 16),
      );
      final ids = plan.valueOrNull!.map((e) => e.id).toList();
      expect(ids.first, 'h264_nvenc');
      expect(ids.last, 'libx264');
      expect(ids, isNot(contains('h264_videotoolbox')));
      await logs.dispose();
    });
  });

  group('AtomicOutput / OutputPlanner', () {
    test('planner avoids overwriting existing files and keeps part on same dir',
        () async {
      final fs = TestFs();
      final input = fs.file('clip.mp4');
      File(fs.path('clip_video_only.mp4')).writeAsBytesSync([1]);
      final plan = const OutputPlanner().plan(
        inputPath: input,
        isPreview: false,
        profile: ProfileRegistry.of(ContainerFormat.mp4),
        overwrite: false,
        kinds: [OutputKind.video, OutputKind.audio],
      );
      expect(plan.length, 2);
      expect(plan.first.finalPath, endsWith('clip_video_only_1.mp4'));
      expect(plan.first.partPath, '${plan.first.finalPath}.part');
      expect(plan.last.finalPath, endsWith('clip_audio.ogg'));
      fs.dispose();
    });

    test('commit never deletes the original and restores it on failure',
        () async {
      final fs = TestFs();
      final target = OutputTarget(
        kind: OutputKind.video,
        finalPath: fs.path('out.mp4'),
        partPath: fs.path('out.mp4.part'),
      );
      File(target.finalPath).writeAsBytesSync([1]);
      File(target.partPath).writeAsBytesSync([2, 3]);

      final blocked = await AtomicOutput.commit(target, overwrite: false);
      expect(blocked.failureOrNull?.kind, FailureKind.io);
      expect(File(target.finalPath).readAsBytesSync(), [1],
          reason: 'a refused overwrite must leave the user file untouched');
      expect(File(target.partPath).existsSync(), isTrue,
          reason: 'the produced part is kept for diagnostics');

      final forced = await AtomicOutput.commit(target, overwrite: true);
      expect(forced.valueOrNull, 2);
      expect(File(target.finalPath).readAsBytesSync(), [2, 3]);
      expect(File(target.partPath).existsSync(), isFalse);
      expect(Directory(fs.dir.path)
              .listSync()
              .where((e) => e.path.contains('.bak')),
          isEmpty,
          reason: 'backups are released only after full success');

      File(target.finalPath).writeAsBytesSync([7, 7, 7]);
      File(target.partPath).writeAsBytesSync([]);
      final empty = await AtomicOutput.commit(target, overwrite: true);
      expect(empty.isErr, isTrue);
      expect(File(target.finalPath).readAsBytesSync(), [7, 7, 7],
          reason: 'validation happens before any mutation');
      fs.dispose();
    });

    test('commitAll is all-or-nothing and keeps user files recoverable',
        () async {
      final fs = TestFs();
      final first = OutputTarget(
        kind: OutputKind.video,
        finalPath: fs.path('a.mp4'),
        partPath: fs.path('a.mp4.part'),
      );
      final second = OutputTarget(
        kind: OutputKind.audio,
        finalPath: fs.path('b.ogg'),
        partPath: fs.path('b.ogg.part'),
      );
      File(first.finalPath).writeAsBytesSync([1, 1]);
      File(first.partPath).writeAsBytesSync([2, 2]);
      // The second part is intentionally missing.
      final failure =
          await AtomicOutput.commitAll([first, second], overwrite: true);
      expect(failure.isErr, isTrue);
      expect(File(first.finalPath).readAsBytesSync(), [1, 1]);
      expect(File(first.partPath).existsSync(), isTrue);
      expect(Directory(fs.dir.path)
              .listSync()
              .where((e) => e.path.contains('.bak')),
          isEmpty);

      File(second.partPath).writeAsBytesSync([3, 3]);
      final success =
          await AtomicOutput.commitAll([first, second], overwrite: true);
      expect(success.isOk, isTrue);
      expect(success.valueOrNull!.map((a) => a.bytes), [2, 2]);
      expect(File(first.finalPath).readAsBytesSync(), [2, 2]);
      expect(File(second.finalPath).readAsBytesSync(), [3, 3]);
      fs.dispose();
    });

    test('pre-flight writability check rejects invalid output directories',
        () async {
      final fs = TestFs();
      expect((await AtomicOutput.validateWritable(fs.dir.path)).isOk, isTrue);
      expect(
          (await AtomicOutput.validateWritable(fs.path('missing'))).isErr,
          isTrue);
      fs.dispose();
    });
  });

  group('WaveformService', () {
    test('content-addressed cache hit, invalidation and prune', () async {
      final rig = ProcessorRig();
      final first = await rig.waveforms.ensure(rig.input);
      expect(first.isOk, isTrue);
      expect(File(first.valueOrNull!).existsSync(), isTrue);
      final callsAfterFirst = rig.runner.countFor('waveform');
      final second = await rig.waveforms.ensure(rig.input);
      expect(second.valueOrNull, first.valueOrNull);
      expect(rig.runner.countFor('waveform'), callsAfterFirst);
      expect(rig.waveforms.cacheStats.hits, 1);

      await File(rig.input).writeAsBytes(List<int>.filled(900, 7));
      await rig.waveforms.ensure(rig.input);
      expect(rig.runner.countFor('waveform'), callsAfterFirst + 1);
      await rig.dispose();
    });
  });

  group('MediaProcessor end-to-end', () {
    test('successful full task commits both outputs atomically', () async {
      final rig = ProcessorRig(
          config: const MediaProcessorConfig(enableHardware: false));
      final progress = <double>[];
      final result = await rig.processor.execute(rig.request(),
          onProgress: (p) => progress.add(p.fraction));
      expect(result.status, TaskStatus.completed);
      expect(result.outputs.length, 2);
      expect(result.stages.length, 2);
      expect(result.outputs.fold<int>(0, (s, o) => s + o.bytes), 192);
      for (final output in result.outputs) {
        expect(File(output.path).existsSync(), isTrue);
        expect(File('${output.path}.part').existsSync(), isFalse);
      }
      expect(progress, isNotEmpty);
      for (var i = 1; i < progress.length; i++) {
        expect(progress[i], greaterThanOrEqualTo(progress[i - 1]));
      }
      expect(rig.processor.metrics.snapshot()['tasks'], 1);
      expect(rig.processor.metrics.snapshot()['stages'], 2);
      rig.flushLogs();
      expect(rig.logs.exportText(), contains('[INFO'));
      expect(rig.logs.exportText(), isNot(contains('❌')));
      for (final output in result.outputs) {
        expect(rig.logs.exportText(), contains(output.path),
            reason: 'the log must state where each output was written');
      }
      await rig.dispose();
    });

    test('encoder failure falls back to the next candidate', () async {
      var videoCalls = 0;
      final rig = ProcessorRig(
        config: const MediaProcessorConfig(
            enableHardware: true, enableStreamCopy: false),
        byLabel: {
          'encoder-list': const ScriptedOutcome(stdoutLines: [
            ' V....D h264_nvenc NVIDIA',
            ' V....D libx264 x264',
          ]),
        },
        responder: (request, index) {
          if (request.label == 'encoder-list') {
            return const ScriptedOutcome(stdoutLines: [
              ' V....D h264_nvenc NVIDIA',
              ' V....D libx264 x264',
            ]);
          }
          if (request.label == 'ffprobe') {
            return ScriptedOutcome(stdoutLines: [sampleProbeJson()]);
          }
          if (request.label == 'video') {
            videoCalls++;
            return ScriptedOutcome(
              exitCode: videoCalls == 1 ? 1 : 0,
              writeOutputBytes: videoCalls == 1 ? 0 : 128,
            );
          }
          return const ScriptedOutcome(writeOutputBytes: 64);
        },
      );
      final result = await rig.processor.execute(rig.request());
      expect(result.status, TaskStatus.completed,
          reason: result.failure?.message ?? rig.logs.exportText());
      expect(videoCalls, 2);
      expect(rig.processor.metrics.snapshot()['encoderFallbacks'], 1);
      expect(result.stages.first.attempts, 2);
      await rig.dispose();
    });

    test('audio failure rolls back already encoded video output', () async {
      final rig = ProcessorRig(
        config: const MediaProcessorConfig(enableHardware: false),
        byLabel: {
          'audio': const ScriptedOutcome(exitCode: 1, writeOutputBytes: 64),
        },
      );
      final result = await rig.processor.execute(rig.request());
      expect(result.status, TaskStatus.failed);
      expect(result.failure?.kind, FailureKind.processFailed);
      expect(Directory(rig.fs.dir.path)
          .listSync()
          .where((e) => e.path.endsWith('.part')), isEmpty);
      expect(
          Directory(rig.fs.dir.path)
              .listSync()
              .where((e) => e.path.endsWith('.mp4') &&
                  !e.path.endsWith('input.mp4')),
          isEmpty);
      await rig.dispose();
    });

    test('cancellation, timeout, missing binary, bad input and busy guard',
        () async {
      final cancelled = ProcessorRig();
      final cancelToken = CancellationToken()..cancel();
      final r1 = await cancelled.processor.execute(
        cancelled.request(),
        cancellation: cancelToken,
      );
      expect(r1.status, TaskStatus.cancelled);
      await cancelled.dispose();

      final timeout = ProcessorRig(
        config: const MediaProcessorConfig(enableHardware: false),
        byLabel: {
          'video': const ScriptedOutcome(timedOut: true, exitCode: -1),
          'audio': const ScriptedOutcome(timedOut: true, exitCode: -1),
        },
      );
      final r2 = await timeout.processor.execute(timeout.request());
      expect(r2.status, TaskStatus.failed);
      expect(r2.failure?.kind, FailureKind.timeout);
      expect(timeout.processor.metrics.snapshot()['timeouts'],
          greaterThanOrEqualTo(1));
      await timeout.dispose();

      final missing = ProcessorRig();
      missing.binaries.failVerification = true;
      final r3 = await missing.processor.execute(missing.request());
      expect(r3.failure?.kind, FailureKind.binaryUnavailable);
      await missing.dispose();

      final badInput = ProcessorRig();
      final r4 = await badInput.processor.execute(TaskRequest(
        kind: TaskKind.full,
        inputPath: badInput.fs.path('nope.mp4'),
        container: ContainerFormat.mp4,
        quality: QualityPreset.balanced,
      ));
      expect(r4.failure?.kind, FailureKind.invalidInput);
      await badInput.dispose();

      final busy = ProcessorRig(
        config: const MediaProcessorConfig(enableHardware: false),
        byLabel: {
          'video': const ScriptedOutcome(
              writeOutputBytes: 128, delay: Duration(milliseconds: 40)),
        },
      );
      final futures = [
        busy.processor.execute(busy.request()),
        busy.processor.execute(busy.request()),
      ];
      final results = await Future.wait(futures);
      expect(results.where((r) => r.status == TaskStatus.completed).length, 1);
      expect(results.where((r) => r.failure?.kind == FailureKind.busy).length,
          1);
      await busy.dispose();
    });

    test('stream-copy fast path is used when codecs and transform allow it',
        () async {
      final rig = ProcessorRig(
        config: const MediaProcessorConfig(
            enableHardware: false, enableStreamCopy: true),
        byLabel: {
          'ffprobe': ScriptedOutcome(
              stdoutLines: [sampleProbeJson(videoCodec: 'h264', audioCodec: 'vorbis')]),
        },
      );
      final result = await rig.processor.execute(rig.request());
      expect(result.status, TaskStatus.completed);
      expect(result.stages.every((s) => s.streamCopy), isTrue);
      expect(rig.processor.metrics.snapshot()['streamCopies'], 2);
      await rig.dispose();
    });

    test('preview task validates clip range against media duration', () async {
      final rig = ProcessorRig(
          config: const MediaProcessorConfig(enableHardware: false));
      final invalid = await rig.processor.execute(rig.request(
        kind: TaskKind.preview,
        range: const ClipRange(startMs: 20000, endMs: 30000),
      ));
      expect(invalid.failure?.kind, FailureKind.invalidInput);
      final valid = await rig.processor.execute(rig.request(
        kind: TaskKind.preview,
        range: const ClipRange(startMs: 1000, endMs: 4000),
      ));
      expect(valid.status, TaskStatus.completed);
      final videoArgs = rig.runner.requests
          .firstWhere((r) => r.label == 'video')
          .arguments;
      expect(videoArgs, contains('-ss'));
      expect(videoArgs, contains('-t'));
      await rig.dispose();
    });
  });
}
