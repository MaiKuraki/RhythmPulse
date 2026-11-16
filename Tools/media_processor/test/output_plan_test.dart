import 'package:flutter_test/flutter_test.dart';

import '../lib/domain/encoding.dart';
import '../lib/domain/output_plan.dart';
import 'helpers/fakes.dart';

void main() {
  test('resolution tiers classify 720p / 1080p / 2K / 4K / SD', () {
    expect(sampleMedia(width: 1280, height: 720).resolution.tier, '720p');
    expect(sampleMedia(width: 1920, height: 1080).resolution.tier, '1080p');
    expect(sampleMedia(width: 2560, height: 1440).resolution.tier, '2K');
    expect(sampleMedia(width: 3840, height: 2160).resolution.tier, '4K');
    expect(sampleMedia(width: 640, height: 480).resolution.tier, '<720p');
  });

  group('OutputPlan resolution math', () {
    test('follow keeps the source untouched', () {
      final media = sampleMedia(width: 3840, height: 2160);
      final plan = OutputPlan.build(media, OutputResolution.follow);
      expect(plan.target.width, 3840);
      expect(plan.target.height, 2160);
      expect(plan.transform, isFalse);
      expect(plan.upscale, isFalse);
      expect(plan.downscale, isFalse);
    });

    test('16:9 4K maps to the exact 720p–4K targets', () {
      final media = sampleMedia(width: 3840, height: 2160);
      for (final entry in <OutputResolution, List<int>>{
        OutputResolution.p720: [1280, 720],
        OutputResolution.p1080: [1920, 1080],
        OutputResolution.p1440: [2560, 1440],
        OutputResolution.p2160: [3840, 2160],
      }.entries) {
        final plan = OutputPlan.build(media, entry.key);
        expect(plan.target.width, entry.value[0],
            reason: '${entry.key.name} width');
        expect(plan.target.height, entry.value[1],
            reason: '${entry.key.name} height');
      }
    });

    test('4:3 source preserves the display aspect ratio with even width', () {
      final media = sampleMedia(width: 1440, height: 1080);
      final plan = OutputPlan.build(media, OutputResolution.p720);
      expect(plan.target.height, 720);
      expect(plan.target.width, 960);
      expect(plan.target.width.isEven, isTrue);
    });

    test('audio-only input has no video target', () {
      final media = sampleMedia(video: false, audio: true);
      final plan = OutputPlan.build(media, OutputResolution.p2160);
      expect(plan.hasVideo, isFalse);
      expect(plan.target.isEmpty, isTrue);
      expect(plan.transform, isFalse);
      expect(plan.upscale, isFalse);
    });
  });

  group('professional advisories', () {
    final profile = ProfileRegistry.of(ContainerFormat.mp4);
    List<InsightKind> kinds(List<Insight> insights) =>
        insights.map((i) => i.kind).toList();

    test('upscaling a sub-720p source warns and explains why', () {
      final media = sampleMedia(width: 640, height: 480);
      final plan = OutputPlan.build(media, OutputResolution.p1080);
      final insights = buildInsights(media, plan, profile);
      expect(kinds(insights), contains(InsightKind.upscale));
      expect(kinds(insights), contains(InsightKind.lowResolutionSource));
      final upscale = insights.firstWhere((i) => i.kind == InsightKind.upscale);
      expect(upscale.level, InsightLevel.warning);
      expect(upscale.args.length, 2);
    });

    test('downscaling reports both sizes without a warning', () {
      final media = sampleMedia(width: 3840, height: 2160);
      final plan = OutputPlan.build(media, OutputResolution.p720);
      final insights = buildInsights(media, plan, profile);
      expect(kinds(insights), contains(InsightKind.downscale));
      expect(insights.first.level, InsightLevel.info);
      expect(kinds(insights), isNot(contains(InsightKind.upscale)));
    });

    test('HDR and lossless stream copy are surfaced', () {
      final hdr = sampleMedia(hdr: true);
      final hdrInsights = buildInsights(
          hdr, OutputPlan.build(hdr, OutputResolution.follow), profile);
      expect(kinds(hdrInsights), contains(InsightKind.hdrToneMap));

      final plain = sampleMedia();
      final plainInsights = buildInsights(
          plain, OutputPlan.build(plain, OutputResolution.follow), profile);
      expect(kinds(plainInsights), contains(InsightKind.streamCopy));
    });

    test('audio-only input reports that video settings are hidden', () {
      final media = sampleMedia(video: false, audio: true);
      final plan = OutputPlan.build(media, OutputResolution.follow);
      final insights = buildInsights(media, plan, profile);
      expect(kinds(insights), [InsightKind.audioOnly]);
    });
  });
}
