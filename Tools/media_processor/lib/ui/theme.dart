// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

/// 8dp grid. All spacing in `lib/ui` comes from here so cards, sections and
/// controls stay visually aligned when composed in different places.
abstract final class AppSpacing {
  static const double xs = 4;
  static const double s = 8;
  static const double m = 16;
  static const double l = 24;
  static const double xl = 32;

  static const double cardPadding = 24;
  static const double pagePadding = 24;
  static const double compactPagePadding = 12;
  static const double sectionGap = 16;

  /// Wide windows get a two-column desktop layout instead of a centered
  /// single column surrounded by dead space.
  static const double maxContentWidth = 1760;
  static const double compactBreakpoint = 760;
  static const double twoColumnBreakpoint = 1200;
  static const int primaryColumnFlex = 6;
  static const int secondaryColumnFlex = 4;
  static const double compactLogHeight = 220;
  static const double regularLogHeight = 300;
  static const double wideLogHeight = 460;
  static const double controlHeight = 44;
  static const double toolbarHeight = 36;
  static const double microControlHeight = 28;
  static const double corner = 16;

  static const EdgeInsets cardInsets = EdgeInsets.all(cardPadding);
}

/// One design system for both brightness modes. Every color used by the UI is
/// derived from [ColorScheme], so light/dark and future dynamic color all work
/// without touching individual widgets.
abstract final class AppTheme {
  static ThemeData light() => _build(Brightness.light);
  static ThemeData dark() => _build(Brightness.dark);

  static ThemeData _build(Brightness brightness) {
    final scheme = ColorScheme.fromSeed(
      seedColor: Colors.deepPurple,
      brightness: brightness,
    );
    final shape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(AppSpacing.corner),
    );
    final buttonShape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(12),
    );
    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: scheme.surface,
      cardTheme: CardThemeData(
        color: scheme.surfaceContainerLow,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: shape,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: ButtonStyle(
          minimumSize: const WidgetStatePropertyAll(
              Size(0, AppSpacing.controlHeight)),
          shape: WidgetStatePropertyAll(buttonShape),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: ButtonStyle(
          minimumSize: const WidgetStatePropertyAll(
              Size(0, AppSpacing.controlHeight)),
          shape: WidgetStatePropertyAll(buttonShape),
        ),
      ),
      segmentedButtonTheme: SegmentedButtonThemeData(
        style: ButtonStyle(
          visualDensity: VisualDensity.compact,
          minimumSize: const WidgetStatePropertyAll(
              Size(0, AppSpacing.controlHeight)),
          shape: WidgetStatePropertyAll(buttonShape),
        ),
      ),
      dropdownMenuTheme: DropdownMenuThemeData(
        inputDecorationTheme: InputDecorationTheme(
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
          // Pin the field to the same 44dp height as segmented buttons and
          // switches so every form control shares one baseline.
          constraints: const BoxConstraints.tightFor(
              height: AppSpacing.controlHeight),
          contentPadding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.m),
          isDense: true,
        ),
      ),
      listTileTheme: const ListTileThemeData(
        contentPadding: EdgeInsets.zero,
        visualDensity: VisualDensity.compact,
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: scheme.primary,
        linearTrackColor: scheme.surfaceContainerHighest,
      ),
      dividerTheme: DividerThemeData(color: scheme.outlineVariant),
    );
  }
}
