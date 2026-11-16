import 'package:flutter_test/flutter_test.dart';

import '../lib/domain/encoding.dart';
import '../lib/domain/media.dart';
import '../lib/domain/task.dart';
import 'helpers/fakes.dart';

void main() {
  group('ClipRange', () {
    test('validation, clamping and duration arithmetic', () {
      expect(const ClipRange(startMs: -1, endMs: 10).isValid, isFalse);
      expect(const ClipRange(startMs: 10, endMs: 10).isValid, isFalse);
      const range = ClipRange(startMs: 1500, endMs: 4500);
      expect(range.durationMs, 3000);
      expect(range.durationSeconds, 3.0);
      expect(range.clampedTo(2000).endMs, 2000);
      expect(range.clampedTo(2000).startMs, 1500);
    });
  });

  group('ProgressAggregator', () {
    test('weighted, monotonic and clamp-safe', () {
      final a = ProgressAggregator({'video': 0.75, 'audio': 0.25});
      expect(a.update('video', 0.5), closeTo(0.375, 1e-9));
      expect(a.update('video', 0.1), closeTo(0.375, 1e-9),
          reason: 'a second ffmpeg must not rewind the bar');
      expect(a.update('audio', 1.5), closeTo(0.625, 1e-9));
      expect(a.complete('video'), 1.0);
      a.reset();
      expect(a.fraction, 0.0);
      expect(a.update('unknown', 5), 0.0);
    });
  });

  group('ProfileRegistry', () {
    test('every container/preset/resolution tier yields valid settings', () {
      for (final format in ProfileRegistry.supported) {
        final profile = ProfileRegistry.of(format);
        expect(profile.muxer, isNotEmpty);
        expect(profile.audioMuxer, isNotEmpty);
        for (final preset in QualityPreset.values) {
          for (final height in [2160, 1440, 1080, 720, 240]) {
            final settings = profile.settingsFor(height, preset);
            expect(settings.constantQuality, greaterThan(0));
            expect(settings.maxBitrateKbps, greaterThan(0));
            expect(settings.speedTier, inInclusiveRange(0, 2));
            expect(settings.cpuUsed, greaterThan(0));
          }
        }
      }
      expect(ProfileRegistry.of(ContainerFormat.mp4).needsFastStart, isTrue);
      expect(ProfileRegistry.of(ContainerFormat.webm).needsFastStart, isFalse);
    });
  });

  group('MediaInfo stream-copy decisions', () {
    final mp4 = ProfileRegistry.of(ContainerFormat.mp4);

    test('copy allowed only when codec, pixel format and transform match', () {
      final h264 = sampleMedia();
      expect(h264.canCopyVideoFor(mp4, needsTransform: false), isTrue);
      expect(h264.canCopyVideoFor(mp4, needsTransform: true), isFalse);
      expect(
          sampleMedia(videoCodec: 'vp9')
              .canCopyVideoFor(mp4, needsTransform: false),
          isFalse);
      expect(
          sampleMedia(pixelFormat: 'yuv444p')
              .canCopyVideoFor(mp4, needsTransform: false),
          isFalse);
      expect(h264.canCopyAudioFor(mp4), isFalse);
      expect(sampleMedia(audioCodec: 'vorbis').canCopyAudioFor(mp4), isTrue);
    });

    test('fingerprint changes with size or mtime', () {
      final a = sampleMedia();
      expect(a.fingerprint, isNot(equals(a.path)));
      expect(a.fingerprint, contains('1024'));
    });
  });

  group('TaskRequest validation', () {
    test('rejects bad ranges, empty input and invalid concurrency', () {
      expect(
          const TaskRequest(
            kind: TaskKind.full,
            inputPath: 'x.mp4',
            container: ContainerFormat.mp4,
            quality: QualityPreset.balanced,
          ).validate(),
          isTrue);
      expect(
          const TaskRequest(
            kind: TaskKind.preview,
            inputPath: 'x.mp4',
            container: ContainerFormat.mp4,
            quality: QualityPreset.balanced,
          ).validate(),
          isFalse);
      expect(
          const TaskRequest(
            kind: TaskKind.full,
            inputPath: '',
            container: ContainerFormat.mp4,
            quality: QualityPreset.balanced,
          ).validate(),
          isFalse);
      expect(
          const TaskRequest(
            kind: TaskKind.full,
            inputPath: 'x.mp4',
            container: ContainerFormat.mp4,
            quality: QualityPreset.balanced,
            maxConcurrentEncodes: 0,
          ).validate(),
          isFalse);
    });
  });
}
