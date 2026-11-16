// ignore_for_file: deprecated_member_use

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../lib/core/log.dart';
import '../lib/domain/encoding.dart';
import '../lib/domain/output_plan.dart';
import '../lib/ui/theme.dart';
import '../lib/ui/widgets/control_panel.dart';
import '../lib/ui/widgets/media_info_card.dart';
import 'helpers/app_fixture.dart';
import 'helpers/fakes.dart';
import 'helpers/rig.dart';

void main() {
  test('design system provides light and dark M3 themes', () {
    final light = AppTheme.light();
    final dark = AppTheme.dark();
    expect(light.useMaterial3, isTrue);
    expect(dark.useMaterial3, isTrue);
    expect(light.brightness, Brightness.light);
    expect(dark.brightness, Brightness.dark);
    expect(light.scaffoldBackgroundColor,
        isNot(equals(dark.scaffoldBackgroundColor)));
    expect(light.cardTheme.color, isNot(equals(dark.cardTheme.color)));
  });

  for (final size in [
    const Size(640, 800),
    const Size(1024, 768),
    const Size(1600, 1000),
    const Size(1920, 1080),
  ]) {
    testWidgets(
        'layout $size with a video selected: aligned controls, no overflow',
        (tester) async {
      final rig = ProcessorRig();
      final bundle = MemoryAssetBundle(loadUiAssets());
      await tester.binding.setSurfaceSize(size);
      addTearDown(() async {
        await tester.binding.setSurfaceSize(null);
        await rig.dispose();
      });

      await tester.pumpWidget(buildTestApp(rig, bundle,
          initialMedia: sampleMedia(width: 1920, height: 1080)));
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull,
          reason: 'no RenderFlex overflow / layout exception at $size');
      expect(find.byKey(const Key('empty-state')), findsNothing);
      expect(find.byType(Card).evaluate().length, greaterThanOrEqualTo(4));
      expect(find.byType(DropdownMenu<QualityPreset>), findsOneWidget);
      expect(find.byType(DropdownMenu<OutputResolution>), findsOneWidget);
      expect(find.byType(PopupMenuButton<LogLevel?>), findsOneWidget);
      expect(find.byType(SegmentedButton<ContainerFormat>), findsOneWidget);
      expect(find.byType(Switch), findsNothing);
      expect(find.byType(Shortcuts), findsWidgets);
      expect(find.byType(Actions), findsWidgets);

      // Every form control shares one 44dp baseline.
      final segmented = tester
          .getSize(find.byType(SegmentedButton<ContainerFormat>))
          .height;
      final dropdown =
          tester.getSize(find.byType(DropdownMenu<QualityPreset>)).height;
      expect(segmented, closeTo(AppSpacing.controlHeight, 4));
      expect(dropdown, closeTo(AppSpacing.controlHeight, 4));
      expect((segmented - dropdown).abs(), lessThanOrEqualTo(4));

      // Resolution labels are short and the exact pixel size is shown.
      expect(find.byWidgetPredicate((widget) =>
          widget is Text &&
          widget.data != null &&
          widget.data!.contains('1920×1080')), findsWidgets);

      expect(tester.getSize(find.byType(PopupMenuButton<LogLevel?>)).height,
          closeTo(AppSpacing.toolbarHeight, 2));

      final wide = size.width >= AppSpacing.twoColumnBreakpoint;
      final compact = size.width < AppSpacing.compactBreakpoint;
      final expectedLogHeight = wide
          ? AppSpacing.wideLogHeight
          : compact
              ? AppSpacing.compactLogHeight
              : AppSpacing.regularLogHeight;
      expect(tester.getSize(find.byKey(const Key('log-panel'))).height,
          expectedLogHeight);

      final contentWidth =
          tester.getSize(find.byKey(const Key('content-root'))).width;
      expect(contentWidth, lessThanOrEqualTo(AppSpacing.maxContentWidth));
      if (wide) {
        expect(contentWidth, greaterThan(size.width * 0.8));
        expect(find.byType(Row), findsWidgets);
      }
    });
  }

  testWidgets('no file selected shows only the empty state', (tester) async {
    final rig = ProcessorRig();
    final bundle = MemoryAssetBundle(loadUiAssets());
    addTearDown(rig.dispose);
    await tester.pumpWidget(buildTestApp(rig, bundle));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('empty-state')), findsOneWidget);
    expect(find.byType(ControlPanel), findsNothing);
    expect(find.byType(MediaInfoCard), findsNothing);
    expect(find.byType(DropdownMenu<OutputResolution>), findsNothing);
    expect(find.byType(SegmentedButton<ContainerFormat>), findsNothing);
    // Status and log stay visible; they are global, not per-source.
    expect(find.byKey(const Key('log-panel')), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('audio-only source hides all video modules in the full app',
      (tester) async {
    final rig = ProcessorRig();
    final bundle = MemoryAssetBundle(loadUiAssets());
    addTearDown(rig.dispose);
    await tester.pumpWidget(buildTestApp(rig, bundle,
        initialMedia: sampleMedia(video: false, audio: true)));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(find.byType(SegmentedButton<ContainerFormat>), findsNothing);
    expect(find.byType(DropdownMenu<OutputResolution>), findsNothing);
    expect(find.byType(DropdownMenu<QualityPreset>), findsOneWidget);
    expect(find.byWidgetPredicate((widget) =>
        widget is Text &&
        widget.data != null &&
        widget.data!.contains('Audio quality')), findsWidgets);
  });

  testWidgets('audio-only control panel hides every video setting',
      (tester) async {
    final bundle = MemoryAssetBundle(loadUiAssets());
    final media = sampleMedia(video: false, audio: true);
    final plan = OutputPlan.build(media, OutputResolution.follow);
    final insights = buildInsights(
        media, plan, ProfileRegistry.of(ContainerFormat.mp4));
    await tester.pumpWidget(localizedApp(
      bundle,
      const Locale('en', 'US'),
      Scaffold(
        body: SingleChildScrollView(
          child: ControlPanel(
            isProcessing: false,
            isAudioOnly: true,
            container: ContainerFormat.mp4,
            quality: QualityPreset.balanced,
            outputResolution: OutputResolution.follow,
            targetSize: '-',
            insights: insights,
            showPreview: false,
            onContainerChanged: (_) {},
            onQualityChanged: (_) {},
            onOutputResolutionChanged: (_) {},
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.byType(SegmentedButton<ContainerFormat>), findsNothing);
    expect(find.byType(DropdownMenu<OutputResolution>), findsNothing);
  });

  testWidgets('sub-720p source with 4K output shows an upscale warning',
      (tester) async {
    final bundle = MemoryAssetBundle(loadUiAssets());
    final media = sampleMedia(width: 640, height: 480);
    final plan = OutputPlan.build(media, OutputResolution.p2160);
    final insights = buildInsights(
        media, plan, ProfileRegistry.of(ContainerFormat.mp4));
    await tester.pumpWidget(localizedApp(
      bundle,
      const Locale('en', 'US'),
      Scaffold(
        body: SingleChildScrollView(
          child: ControlPanel(
            isProcessing: false,
            isAudioOnly: false,
            container: ContainerFormat.mp4,
            quality: QualityPreset.balanced,
            outputResolution: OutputResolution.p2160,
            targetSize: '3840×2160',
            insights: insights,
            showPreview: false,
            onContainerChanged: (_) {},
            onQualityChanged: (_) {},
            onOutputResolutionChanged: (_) {},
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.textContaining('Upscaling adds no detail'), findsOneWidget);
  });
}
