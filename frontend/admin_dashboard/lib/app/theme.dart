import 'package:flutter/material.dart';

/// Brand colours: midnight blue for structure, dark blue for actions, teal for accents and success, white surfaces.
abstract final class AppColors {
  static const midnight = Color(0xFF0B1A33);
  static const midnightDeep = Color(0xFF071225);
  static const darkBlue = Color(0xFF1E3A8A);
  static const teal = Color(0xFF0F9488);
  static const tealBright = Color(0xFF2DD4BF);
  static const white = Color(0xFFFFFFFF);
}

/// The dashboard's single light theme: midnight-blue frame, white surfaces, blue actions and teal accents.
abstract final class AppTheme {
  static ThemeData light() => _build(
    ColorScheme.fromSeed(seedColor: AppColors.darkBlue).copyWith(
      primary: AppColors.darkBlue,
      onPrimary: AppColors.white,
      primaryContainer: const Color(0xFFDCE6FA),
      onPrimaryContainer: AppColors.midnight,
      secondary: AppColors.teal,
      onSecondary: AppColors.white,
      tertiary: AppColors.darkBlue,
      onTertiary: AppColors.white,
      secondaryContainer: const Color(0xFFCCF1EC),
      onSecondaryContainer: const Color(0xFF053B36),
      surface: AppColors.white,
      onSurface: AppColors.midnight,
      onSurfaceVariant: const Color(0xFF4A5A74),
      surfaceContainerLowest: const Color(0xFFF4F7FC),
      surfaceContainerLow: const Color(0xFFF8FAFD),
      surfaceContainer: const Color(0xFFEEF2F9),
      surfaceContainerHigh: const Color(0xFFE6ECF6),
      surfaceContainerHighest: const Color(0xFFDCE4F1),
      outline: const Color(0xFF8A97AD),
      outlineVariant: const Color(0xFFD7DFEC),
      error: const Color(0xFFC62828),
      onError: AppColors.white,
    ),
  );

  static ThemeData _build(ColorScheme colors) {
    final radius = BorderRadius.circular(12);
    final outline = BorderSide(color: colors.outlineVariant);
    return ThemeData(
      useMaterial3: true,
      colorScheme: colors,
      scaffoldBackgroundColor: colors.surfaceContainerLowest,
      // The frame: a midnight-blue bar with white text and a teal marker on the open tab.
      appBarTheme: AppBarTheme(
        backgroundColor: AppColors.midnight,
        foregroundColor: AppColors.white,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        titleTextStyle: const TextStyle(color: AppColors.white, fontSize: 18, fontWeight: FontWeight.w600),
      ),
      tabBarTheme: TabBarThemeData(
        labelColor: AppColors.white,
        unselectedLabelColor: AppColors.white.withValues(alpha: 0.65),
        indicatorColor: AppColors.tealBright,
        indicatorSize: TabBarIndicatorSize.label,
        labelStyle: const TextStyle(fontWeight: FontWeight.w600),
        dividerColor: Colors.transparent,
        overlayColor: WidgetStatePropertyAll(AppColors.white.withValues(alpha: 0.06)),
      ),
      cardTheme: CardThemeData(
        color: colors.surface,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(borderRadius: radius, side: outline),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: colors.surface,
        shape: RoundedRectangleBorder(borderRadius: radius),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: colors.surfaceContainerLow,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
        enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: outline),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: colors.secondary, width: 2),
        ),
        floatingLabelStyle: WidgetStateTextStyle.resolveWith(
          (states) =>
              TextStyle(color: states.contains(WidgetState.focused) ? colors.secondary : colors.onSurfaceVariant),
        ),
      ),
      textSelectionTheme: TextSelectionThemeData(cursorColor: colors.secondary),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: colors.primary,
          foregroundColor: colors.onPrimary,
          minimumSize: const Size(0, 44),
          padding: const EdgeInsets.symmetric(horizontal: 20),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: colors.primary,
          side: BorderSide(color: colors.outline),
          minimumSize: const Size(0, 44),
          padding: const EdgeInsets.symmetric(horizontal: 20),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        ),
      ),
      textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(foregroundColor: colors.primary)),
      dataTableTheme: DataTableThemeData(
        headingRowColor: WidgetStatePropertyAll(colors.surfaceContainerHigh),
        headingTextStyle: TextStyle(color: colors.onSurface, fontWeight: FontWeight.w600, fontSize: 13),
        dataTextStyle: TextStyle(color: colors.onSurface, fontSize: 14),
        dataRowMinHeight: 52,
        dataRowMaxHeight: 60,
        dividerThickness: 0.6,
        horizontalMargin: 20,
        columnSpacing: 32,
      ),
      chipTheme: ChipThemeData(
        selectedColor: colors.secondaryContainer,
        checkmarkColor: colors.onSecondaryContainer,
        side: outline,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      popupMenuTheme: PopupMenuThemeData(
        color: colors.surface,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8), side: outline),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(color: colors.secondary),
    );
  }
}
