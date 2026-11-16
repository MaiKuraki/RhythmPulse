import 'dart:io' show Platform;

import 'package:flutter_test/flutter_test.dart';

import '../lib/domain/encoding.dart';
import '../lib/domain/media.dart';
import '../lib/domain/task.dart';
import '../lib/platform/encoders.dart';
import '../lib/platform/host.dart';
import '../lib/services/command_builder.dart';
import 'helpers/fakes.dart';
import 'helpers/metrics.dart';

int get _depth => int.tryParse(Platform.environment['COMBO_DEPTH'] ?? '') ?? 2;

final _hosts = [
  HostCapabilities(
      os: HostOs.windows, architecture: CpuArchitecture.x64, logicalCores: 16),
  HostCapabilities(
      os: HostOs.windows, architecture: CpuArchitecture.arm64, logicalCores: 8),
  if (_depth >= 2) ...[
    HostCapabilities(
        os: HostOs.linux, architecture: CpuArchitecture.x64, logicalCores: 32),
    HostCapabilities(
        os: HostOs.macos, architecture: CpuArchitecture.arm64, logicalCores: 8),
  ],
];

void _assertCommon(List<String> args, String output) {
  for (final required in [
    '-hide_banner', '-nostdin', '-nostats', '-progress', 'pipe:1',
    '-loglevel', 'warning', '-y', '-f', '-i'
  ]) {
    expect(args, contains(required), reason: 'missing $required in $args');
  }
  expect(args.last, output);
  expect(args.where((e) => e == '-i').length, 1);
  expect(args.every((e) => e.isNotEmpty), isTrue);
  expect(args.any((e) => e.contains('&&') || e.contains('\n')), isFalse);
  expect(args.where((e) => e == '-f').length, 1);
}

void main() {
  group('FfmpegCommandBuilder exhaustive matrix', () {
    test('every container x encoder x preset x resolution x HDR x clip '
        'x copy x host combination produces a valid command', () {
      const builder = FfmpegCommandBuilder();
      var commands = 0;
      var assertions = 0;
      for (final format in ContainerFormat.values) {
        final profile = ProfileRegistry.of(format);
        for (final host in _hosts) {
          final encoders = [
            ...EncoderCatalog.candidates(profile.video, host),
            null, // stream-copy variant
          ];
          for (final encoder in encoders) {
            for (final quality in QualityPreset.values) {
              final heights = _depth >= 2
                  ? [2160, 1440, 1080, 720]
                  : [1080, 720];
              for (final sourceHeight in heights) {
                final variants = _depth >= 2
                    ? [false, true]
                    : [false];
                for (final hdr in variants) {
                  for (final scaled in variants) {
                    final ranges = _depth >= 2
                        ? <ClipRange?>[null, const ClipRange(startMs: 1000, endMs: 4000)]
                        : <ClipRange?>[null];
                    for (final range in ranges) {
                      final media = sampleMedia(
                        height: sourceHeight,
                        hdr: hdr,
                        path: '/tmp/input file.mp4',
                      );
                      final targetHeight = scaled ? 720 : sourceHeight;
                      final target = Resolution(1920, targetHeight);
                      final copy = encoder == null;
                      for (final kind in OutputKind.values) {
                        final output = '/tmp/out ${commands}_$kind.part';
                        final spec = CommandSpec(
                          input: media.path,
                          output: output,
                          media: media,
                          profile: profile,
                          quality: quality,
                          targetResolution: target,
                          kind: kind,
                          threads: host.planFor(
                              encoder?.vendor ?? GpuVendor.software),
                          range: range,
                          encoder: encoder,
                          streamCopy: copy && kind == OutputKind.video,
                        );
                        final args = builder.build(spec);
                        _assertCommon(args, output);
                        assertions += 12;

                        final inputIndex = args.indexOf('-i');
                        if (range != null) {
                          expect(args.sublist(0, inputIndex), contains('-ss'));
                          expect(args.sublist(0, inputIndex), contains('-t'));
                        } else {
                          expect(args.sublist(0, inputIndex),
                              isNot(contains('-ss')));
                        }

                        if (kind == OutputKind.audio) {
                          expect(args, contains('-vn'));
                          expect(args, contains('0:a:0'));
                          expect(args, isNot(contains('-c:v')));
                          expect(args, contains(profile.audioMuxer));
                        } else {
                          expect(args, contains('-an'));
                          expect(args, contains(profile.muxer));
                          expect(args.where((e) => e == '-c:v').length, 1);
                          if (copy) {
                            expect(args, contains('copy'));
                            expect(args, isNot(contains('-vf')));
                          } else {
                            expect(args, contains(encoder.id));
                            final pipeline =
                                FfmpegCommandBuilder.planVideo(spec);
                            // A zero-copy GPU pipeline keeps frames on the
                            // device and must not force a format download.
                            if (pipeline.inputArgs.isEmpty) {
                              expect(args, contains('-pix_fmt'));
                            } else {
                              expect(args, isNot(contains('-pix_fmt')));
                            }
                            if (hdr) {
                              expect(pipeline.inputArgs,
                                  isNot(contains('-hwaccel')),
                                  reason: 'CPU tonemap forbids GPU decode');
                              expect(pipeline.filter, contains('tonemap'));
                            } else if (!spec.needsScale &&
                                encoder.isHardware &&
                                encoder.hwaccel != null) {
                              expect(pipeline.inputArgs,
                                  contains('-hwaccel'));
                              expect(pipeline.filter, isNull,
                                  reason: 'zero-copy GPU path must have no '
                                      'CPU filter');
                              if (encoder.hwaccelOutputFormat != null) {
                                expect(pipeline.inputArgs,
                                    contains('-hwaccel_output_format'));
                              }
                            }
                            if (spec.needsScale) {
                              expect(pipeline.filter, isNotNull);
                            }
                          }
                          if (profile.needsFastStart) {
                            expect(args, contains('+faststart'));
                          }
                        }
                        commands++;
                      }
                    }
                  }
                }
              }
            }
          }
        }
      }
      expect(commands, greaterThan(100));
      recordMetric('command_matrix', {
        'commands': commands,
        'invariants': assertions,
        'hosts': _hosts.length,
        'depth': _depth,
      });
    });

    test('waveform and encoder-list commands are explicit', () {
      final waveform = FfmpegCommandBuilder.waveform(
          input: 'in.mp4', output: 'out.png.part', width: 64, height: 32);
      expect(waveform, contains('-f'));
      expect(waveform, contains('image2'));
      expect(waveform, contains('png'),
          reason: '.part output must not fall back to MJPEG');
      expect(waveform, contains('-nostdin'));
      expect(waveform.last, 'out.png.part');
      expect(FfmpegCommandBuilder.encoderListArgs, contains('-encoders'));
    });
  });

  group('FfmpegProgress', () {
    test('parses structured output and rejects malformed values', () {
      expect(FfmpegProgress.parse('progress=end', 10), 1.0);
      expect(FfmpegProgress.parse('out_time_us=2500000', 10), 0.25);
      expect(FfmpegProgress.parse('out_time_ms=2500', 10), 0.25);
      // Historic ffmpeg builds emit microseconds under the out_time_ms key.
      expect(FfmpegProgress.parse('out_time_ms=2500000', 10), 0.25);
      expect(FfmpegProgress.parse('out_time=00:00:05.000', 10), 0.5);
      expect(FfmpegProgress.parse('out_time_us=-5', 10), 0.0);
      expect(FfmpegProgress.parse('out_time_us=999999999', 10), 1.0);
      expect(FfmpegProgress.parse('frame=  120 fps=30', 10), isNull);
      expect(FfmpegProgress.parse('out_time_us=abc', 10), isNull);
      expect(FfmpegProgress.parse('out_time=bad', 10), isNull);
      expect(FfmpegProgress.parse('anything', 0), isNull);
      expect(FfmpegProgress.parse('anything', double.nan), isNull);
    });
  });
}
