import 'dart:convert';
import 'dart:io';
import 'dart:ui' show Locale;

import 'package:flutter_test/flutter_test.dart';

import '../lib/core/app_version.dart';
import '../lib/ui/strings.dart';

Map<String, dynamic> _read(String name) {
  final file = File('lib/i18n/$name.json');
  expect(file.existsSync(), isTrue, reason: 'missing $name resource');
  return json.decode(file.readAsStringSync()) as Map<String, dynamic>;
}

int _placeholders(String value) =>
    '%s'.allMatches(value).length;

void main() {
  test('application version is consistent with the manifest', () {
    final manifest = File('pubspec.yaml').readAsStringSync();
    expect(manifest, contains('version: $kAppVersion+1'));
    final rc = File('windows/runner/Runner.rc').readAsStringSync();
    expect(rc, contains('VERSION_AS_STRING "$kAppVersion"'));
    expect(rc, contains('CompanyName", "CycloneGames"'));
  });

  test('both locales validate against the closed key set', () {
    final en = _read('en_US');
    final zh = _read('zh_CN');
    final enStrings = AppStrings.fromJson(const Locale('en', 'US'),
        json.encode(en));
    final zhStrings = AppStrings.fromJson(const Locale('zh', 'CN'),
        json.encode(zh));

    expect(en.keys.toSet(), StringKey.all.toSet());
    expect(zh.keys.toSet(), StringKey.all.toSet());
    for (final key in StringKey.all) {
      expect(enStrings.get(key), isNotEmpty);
      expect(zhStrings.get(key), isNotEmpty);
      expect(_placeholders(en[key] as String),
          _placeholders(zh[key] as String),
          reason: 'placeholder count mismatch for $key');
    }
  });

  test('substitution is deterministic and unknown keys fail loudly', () {
    final strings = AppStrings.fromJson(const Locale('en', 'US'),
        json.encode(_read('en_US')));
    expect(strings.get(StringKey.supportedFormats, ['mp4, mp3']),
        contains('mp4, mp3'));
    expect(strings.get(StringKey.outputs, ['2', '4096']), contains('2'));
    // Arguments containing %s must not be re-substituted.
    expect(strings.get(StringKey.supportedFormats, ['%s']), contains('%s'));
    expect(() => strings.get('notAKey'), throwsStateError);
  });

  test('unsupported locales normalize instead of crashing', () {
    expect(AppStrings.normalize(const Locale('zh', 'TW')),
        const Locale('zh', 'CN'));
    expect(AppStrings.normalize(const Locale('en', 'GB')),
        const Locale('en', 'US'));
    expect(AppStrings.normalize(const Locale('fr', 'FR')),
        const Locale('en', 'US'));
    expect(AppStrings.assetFor(const Locale('zh', 'TW')),
        'lib/i18n/zh_CN.json');
    expect(AppStringsDelegate().isSupported(const Locale('zh', 'TW')), isTrue);
  });

  test('missing translations degrade to English and are reported', () {
    final english = json.encode(_read('en_US'));
    final partial = json.encode(
        Map<String, dynamic>.from(_read('zh_CN'))..remove('appTitle'));
    final strings = AppStrings.fromJson(
      const Locale('zh', 'CN'),
      partial,
      fallbackSource: english,
    );
    expect(strings.missingKeys, contains(StringKey.appTitle));
    expect(strings.get(StringKey.appTitle), isNotEmpty);
    expect(strings.degradedKeys, contains(StringKey.appTitle));
    expect(strings.validate(fallbackSource: english),
        contains('missing:appTitle'));
  });
}
