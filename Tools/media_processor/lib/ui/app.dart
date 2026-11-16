// Flutter 3.50 deprecates the bundled Material package in favour of the
// opt-in material_ui package; this app keeps the bundled widgets.
// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../domain/media.dart';
import 'app_scope.dart';
import 'home/home_screen.dart';
import 'strings.dart';
import 'theme.dart';

final class MediaProcessorApp extends StatelessWidget {
  const MediaProcessorApp({
    super.key,
    required this.services,
    this.assetBundle,
    this.disableSemantics = false,
    this.initialMedia,
  });

  final AppServices services;

  /// Injectable for widget tests that provide in-memory i18n resources.
  final AssetBundle? assetBundle;

  /// Opt-out for Windows builds where a UI Automation client (IME host,
  /// Narrator, remote desktop) makes the engine's AXTree log noisy on resize.
  /// Enabling this removes application semantics for screen readers.
  final bool disableSemantics;

  /// Optional pre-selected source. Used by widget/layout verification and by
  /// future session restore; production normally starts with no selection.
  final MediaInfo? initialMedia;

  @override
  Widget build(BuildContext context) {
    final app = ValueListenableBuilder<Locale>(
      valueListenable: services.locale,
      builder: (context, locale, _) => MaterialApp(
        onGenerateTitle: (context) =>
            AppStrings.maybeOf(context)?.get(StringKey.appTitle) ??
            'Media Processor',
        debugShowCheckedModeBanner: false,
        locale: locale,
        supportedLocales: AppStrings.supportedLocales,
        localizationsDelegates: [
          AppStringsDelegate(bundle: assetBundle),
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        theme: AppTheme.light(),
        darkTheme: AppTheme.dark(),
        themeMode: ThemeMode.system,
        home: HomeScreen(initialMedia: initialMedia),
      ),
    );
    return AppScope(
      services: services,
      child: disableSemantics ? ExcludeSemantics(child: app) : app,
    );
  }
}
