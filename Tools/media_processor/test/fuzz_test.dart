import 'dart:math';

import 'dart:io' show Platform;

import 'package:flutter_test/flutter_test.dart';

import '../lib/core/ring_buffer.dart';
import '../lib/domain/encoding.dart';
import '../lib/domain/media.dart';
import '../lib/domain/task.dart';
import '../lib/platform/encoders.dart';
import '../lib/platform/host.dart';
import '../lib/services/command_builder.dart';
import '../lib/services/probe.dart';
import 'helpers/fakes.dart';
import 'helpers/metrics.dart';

int get _seed => int.tryParse(Platform.environment['TEST_SEED'] ?? '') ?? 1;
int get _cases =>
    int.tryParse(Platform.environment['FUZZ_CASES'] ?? '') ?? 400;
int get _depth => int.tryParse(Platform.environment['COMBO_DEPTH'] ?? '') ?? 2;

const _alphabet = [
  ...['a', 'Z', '0', ' ', '\t', '\n', '=', ':', '/', '\\', '|', '-', '_', '.'],
  ...['中', '文', '\u0000', '\ufffd', '\$', '%', '😀'],
];

String _randomString(Random random, int maxLength) {
  final length = random.nextInt(maxLength + 1);
  final buffer = StringBuffer();
  for (var i = 0; i < length; i++) {
    buffer.write(_alphabet[random.nextInt(_alphabet.length)]);
  }
  return buffer.toString();
}

double _randomDouble(Random random) {
  final values = [
    random.nextDouble() * 1000,
    -random.nextDouble() * 1000,
    0.0,
    double.nan,
    double.infinity,
    -double.infinity,
    1e308,
  ];
  return values[random.nextInt(values.length)];
}

void main() {
  test('randomized inputs preserve all invariants', () {
    final random = Random(_seed);
    final builder = const FfmpegCommandBuilder();
    var assertions = 0;
    var commands = 0;

    for (var i = 0; i < _cases; i++) {
      final noise = _randomString(random, 24 + _depth * 8);

      // Structured progress parsing must never throw or escape [0, 1].
      final fraction = FfmpegProgress.parse(
          'out_time_us=${_randomDouble(random)}', _randomDouble(random));
      if (fraction != null) {
        expect(fraction, inInclusiveRange(0.0, 1.0));
        assertions++;
      }
      expect(FfmpegProgress.parse(noise, 10), anyOf(isNull, isA<double>()));
      assertions++;

      // Probe parser must always produce a structured outcome.
      final probe = MediaProbeService.parse(noise,
          path: noise,
          sizeBytes: random.nextInt(1 << 20),
          modifiedMs: random.nextInt(1 << 30));
      expect(probe.isOk || probe.isErr, isTrue);
      assertions++;

      // Ring buffer stays bounded under arbitrary interleaving.
      final capacity = 1 + random.nextInt(32);
      final ring = RingBuffer<String>(capacity);
      for (var k = 0; k < capacity * 3; k++) {
        ring.add(_randomString(random, 8));
      }
      expect(ring.length, lessThanOrEqualTo(capacity));
      if (ring.length > 0) expect(ring[0], isA<String>());
      assertions++;

      // Progress aggregation is monotonic for any sequence.
      final aggregator =
          ProgressAggregator({'video': random.nextDouble(), 'audio': random.nextDouble()});
      var last = 0.0;
      for (var k = 0; k < 20; k++) {
        final value = aggregator.update(
            random.nextBool() ? 'video' : 'audio', _randomDouble(random));
        expect(value, greaterThanOrEqualTo(last));
        expect(value, inInclusiveRange(0.0, 1.0));
        last = value;
        assertions++;
      }

      // Command construction stays well formed for arbitrary specs.
      final format = ContainerFormat.values[random.nextInt(ContainerFormat.values.length)];
      final profile = ProfileRegistry.of(format);
      final host = HostCapabilities(
        os: HostOs.values[random.nextInt(HostOs.values.length)],
        architecture:
            CpuArchitecture.values[random.nextInt(CpuArchitecture.values.length)],
        logicalCores: 1 + random.nextInt(64),
      );
      final candidates = EncoderCatalog.candidates(profile.video, host);
      final encoder = candidates.isEmpty
          ? null
          : candidates[random.nextInt(candidates.length)];
      final height = 1 + random.nextInt(4320);
      final media = sampleMedia(
        path: noise.isEmpty ? 'x' : noise,
        width: 1 + random.nextInt(3840),
        height: height,
        hdr: random.nextBool(),
        videoCodec: _randomString(random, 6),
        audioCodec: _randomString(random, 6),
        pixelFormat: _randomString(random, 6),
        duration: Duration(milliseconds: random.nextInt(600000)),
      );
      final spec = CommandSpec(
        input: media.path,
        output: '${noise}_out.part',
        media: media,
        profile: profile,
        quality: QualityPreset.values[random.nextInt(QualityPreset.values.length)],
        targetResolution: Resolution(1 + random.nextInt(3840), 1 + random.nextInt(4320)),
        kind: OutputKind.values[random.nextInt(OutputKind.values.length)],
        threads: host.planFor(encoder?.vendor ?? GpuVendor.software),
        range: random.nextBool()
            ? ClipRange(
                startMs: random.nextInt(100000), endMs: random.nextInt(100000))
            : null,
        encoder: encoder,
        streamCopy: encoder == null || random.nextBool(),
        fastStart: random.nextBool(),
      );
      final args = builder.build(spec);
      expect(args, isNotEmpty);
      expect(args.every((e) => e.isNotEmpty), isTrue);
      expect(args, contains('-nostdin'));
      expect(args, contains('-f'));
      expect(args.where((e) => e == '-i').length, 1);
      expect(args.last, spec.output);
      commands++;
      assertions += 6;

      // Config tables always resolve.
      final settings = profile.settingsFor(height, QualityPreset.values[i % 3]);
      expect(settings.speedTier, inInclusiveRange(0, 2));
      assertions++;
    }

    expect(commands, _cases);
    recordMetric('fuzz', {
      'cases': _cases,
      'commands': commands,
      'invariants': assertions,
      'seed': _seed,
      'depth': _depth,
    });
  });
}
