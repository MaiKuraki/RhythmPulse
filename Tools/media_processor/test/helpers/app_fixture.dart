// ignore_for_file: deprecated_member_use
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../lib/domain/media.dart';
import '../../lib/ui/app.dart';
import '../../lib/ui/app_scope.dart';
import '../../lib/ui/strings.dart';
import 'rig.dart';

/// In-memory i18n assets so widget tests never depend on `rootBundle` or the
/// working directory's asset manifest.
final class MemoryAssetBundle extends CachingAssetBundle {
  MemoryAssetBundle(this.data);

  final Map<String, String> data;

  @override
  Future<ByteData> load(String key) async {
    final value = data[key];
    if (value == null) throw FlutterError('missing asset: $key');
    return ByteData.sublistView(Uint8List.fromList(utf8.encode(value)));
  }
}

Map<String, String> loadUiAssets() => {
      'lib/i18n/en_US.json': File('lib/i18n/en_US.json').readAsStringSync(),
      'lib/i18n/zh_CN.json': File('lib/i18n/zh_CN.json').readAsStringSync(),
    };

Widget localizedApp(
  MemoryAssetBundle bundle,
  Locale locale,
  Widget home,
) =>
    MaterialApp(
      locale: locale,
      supportedLocales: AppStrings.supportedLocales,
      localizationsDelegates: [
        AppStringsDelegate(bundle: bundle),
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: home,
    );

Widget buildTestApp(
  ProcessorRig rig,
  MemoryAssetBundle bundle, {
  bool disableSemantics = false,
  MediaInfo? initialMedia,
}) =>
    MediaProcessorApp(
      services: AppServices(
        logs: rig.logs,
        processor: rig.processor,
        probe: rig.probe,
        tasks: rig.tasks,
        waveforms: rig.waveforms,
        host: rig.host,
        locale: ValueNotifier<Locale>(const Locale('en', 'US')),
      ),
      assetBundle: bundle,
      disableSemantics: disableSemantics,
      initialMedia: initialMedia,
    );

int semanticsCount(WidgetTester tester) {
  final root = tester.binding.pipelineOwner.semanticsOwner?.rootSemanticsNode;
  if (root == null) return 0;
  int count(SemanticsNode node) {
    var total = 1;
    node.visitChildren((child) {
      total += count(child);
      return true;
    });
    return total;
  }

  return count(root);
}
