import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:localization/localization.dart';

import 'services/ffmpeg_service.dart';
import 'ui/home/home_screen.dart';

/// Main application entry point
void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  
  // Initialize Localization
  LocalJsonLocalization.delegate.directories = ['lib/i18n'];

  // Initialize FFmpeg Service with the correct root path before any CWD changes occur
  FfmpegService.init(Directory.current.path);

  // Handle process termination signals (Ctrl+C)
  ProcessSignal.sigint.watch().listen((signal) {
    FfmpegProcessManager().killAll().then((_) {
      exit(0);
    });
  });

  if (kDebugMode) {
     _checkEnvironment();
  }

  // Initialize and run the application
  runApp(const MediaProcessingMasterApp());
}

Future<void> _checkEnvironment() async {
    final ffmpegPath = FfmpegService.getBundledFfmpegPath();
    final ffprobePath = FfmpegService.getBundledFfprobePath();
    
    if (kDebugMode) {
      print('Checking FFmpeg environment...');
      print('FFmpeg: $ffmpegPath (${await File(ffmpegPath).exists() ? 'Found' : 'Missing'})');
      print('FFprobe: $ffprobePath (${await File(ffprobePath).exists() ? 'Found' : 'Missing'})');
    }
}

/// Global notifier for app locale
final ValueNotifier<Locale> appLocaleNotifier = ValueNotifier(const Locale('en', 'US'));

/// Root application widget configuring MaterialApp with internationalization support
class MediaProcessingMasterApp extends StatelessWidget {
  const MediaProcessingMasterApp({super.key});

  static const _supportedLocales = [Locale('en', 'US'), Locale('zh', 'CN')];
  
  static final List<LocalizationsDelegate> _localizationsDelegates = [
    LocalJsonLocalization.delegate,
    GlobalMaterialLocalizations.delegate,
    GlobalWidgetsLocalizations.delegate,
    GlobalCupertinoLocalizations.delegate,
  ];

  @override
  Widget build(BuildContext context) {
    
    return ValueListenableBuilder<Locale>(
      valueListenable: appLocaleNotifier,
      builder: (context, locale, _) {
        return MaterialApp(
          title: 'Media Processor',
          debugShowCheckedModeBanner: false,
          locale: locale,
          theme: ThemeData(
            colorScheme: ColorScheme.fromSeed(
              seedColor: Colors.deepPurple,
              brightness: Brightness.light,
              surface: const Color(0xFFF5F5F7), // Apple-like light grey background
            ),
            useMaterial3: true,
            scaffoldBackgroundColor: const Color(0xFFF5F5F7),
            // Note: Using CardThemeData based on compiler error indicating CardTheme mismatch
            cardTheme: CardThemeData(
              elevation: 0, // Flat modern look
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(20),
                side: BorderSide(color: Colors.grey.withAlpha(25), width: 1), 
              ),
              color: Colors.white,
              margin: EdgeInsets.zero,
            ),
            inputDecorationTheme: InputDecorationTheme(
              filled: true,
              fillColor: Colors.grey.shade50,
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: BorderSide.none,
              ),
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: BorderSide.none,
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: const BorderSide(color: Colors.deepPurple, width: 2),
              ),
              contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
            ),
            elevatedButtonTheme: ElevatedButtonThemeData(
              style: ElevatedButton.styleFrom(
                elevation: 2,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(16),
                ),
                padding: const EdgeInsets.symmetric(vertical: 18, horizontal: 32), // Taller buttons
                textStyle: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w600,
                  letterSpacing: 0.5,
                ),
              ),
            ),
          ),
          supportedLocales: _supportedLocales,
          localizationsDelegates: _localizationsDelegates,
          home: const HomeScreen(),
        );
      },
    );
  }
}
