// ignore_for_file: deprecated_member_use

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import '../lib/core/log.dart';
import '../lib/ui/strings.dart';
import '../lib/ui/widgets/log_view.dart';
import 'helpers/app_fixture.dart';
import 'helpers/fakes.dart';
import 'helpers/rig.dart';

void main() {
  testWidgets('unsupported country variant resolves without crashing',
      (tester) async {
    final bundle = MemoryAssetBundle(loadUiAssets());
    await tester.pumpWidget(localizedApp(
      bundle,
      const Locale('zh', 'TW'),
      Builder(
        builder: (context) =>
            Text(AppStrings.of(context).get(StringKey.appTitle)),
      ),
    ));
    await tester.pumpAndSettle();
    expect(find.text('媒体处理器'), findsOneWidget);
  });

  testWidgets('locale switch rebuilds localized content', (tester) async {
    final bundle = MemoryAssetBundle(loadUiAssets());
    for (final entry in <Locale, String>{
      const Locale('en', 'US'): 'Media Processor',
      const Locale('en', 'GB'): 'Media Processor',
      const Locale('zh', 'CN'): '媒体处理器',
    }.entries) {
      await tester.pumpWidget(localizedApp(
        bundle,
        entry.key,
        Builder(
          builder: (context) =>
              Text(AppStrings.of(context).get(StringKey.appTitle)),
        ),
      ));
      await tester.pumpAndSettle();
      expect(find.text(entry.value), findsOneWidget,
          reason: 'locale ${entry.key} must render ${entry.value}');
    }
  });

  testWidgets('log view honours the level filter used for export',
      (tester) async {
    final bundle = MemoryAssetBundle(loadUiAssets());
    final store = LogStore(scheduler: ManualScheduler());
    store.info('source', 'visible-info');
    store.error('source', 'visible-error');
    await tester.pumpWidget(localizedApp(
      bundle,
      const Locale('en', 'US'),
      Scaffold(
        body: SizedBox(
          height: 240,
          child: LogView(
            store: store,
            filter: LogLevel.error,
            onFilterChanged: (_) {},
            onExport: () async {},
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();
    expect(find.textContaining('visible-error'), findsOneWidget);
    expect(find.textContaining('visible-info'), findsNothing);
    expect(store.exportText(level: LogLevel.error),
        contains('visible-error'));
    expect(store.exportText(level: LogLevel.error),
        isNot(contains('visible-info')));
  });

  testWidgets('window resize does not grow the semantics tree', (tester) async {
    final rig = ProcessorRig();
    final bundle = MemoryAssetBundle(loadUiAssets());
    await tester.binding.setSurfaceSize(const Size(900, 700));
    await tester.pumpWidget(buildTestApp(rig, bundle));
    await tester.pumpAndSettle();
    final handle = tester.ensureSemantics();
    await tester.pumpAndSettle();
    final before = semanticsCount(tester);

    for (final size in [
      const Size(1200, 800),
      const Size(700, 600),
      const Size(1600, 900),
    ]) {
      await tester.binding.setSurfaceSize(size);
      await tester.pump();
      await tester.pumpAndSettle();
      expect(semanticsCount(tester), lessThanOrEqualTo(before + 4),
          reason: 'resize to $size must not create/abandon semantics nodes');
    }
    handle.dispose();
    await rig.dispose();
  });

  testWidgets('semantics opt-out removes application semantics nodes',
      (tester) async {
    final rig = ProcessorRig();
    final bundle = MemoryAssetBundle(loadUiAssets());
    await tester
        .pumpWidget(buildTestApp(rig, bundle, disableSemantics: true));
    await tester.pumpAndSettle();
    expect(
        find.byWidgetPredicate(
            (widget) => widget is ExcludeSemantics && widget.excluding),
        findsWidgets);
    final handle = tester.ensureSemantics();
    await tester.pumpAndSettle();
    expect(semanticsCount(tester), lessThanOrEqualTo(2));
    handle.dispose();
    await rig.dispose();
  });
}
