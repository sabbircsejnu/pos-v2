import 'package:flutter/material.dart';

import 'app_colors.dart';

abstract final class AppTheme {
  static ThemeData get light {
    final colorScheme =
        ColorScheme.fromSeed(
          seedColor: AppColors.primaryBrown,
          brightness: Brightness.light,
          surface: AppColors.warmIvory,
        ).copyWith(
          primary: AppColors.primaryBrown,
          onPrimary: AppColors.white,
          secondary: AppColors.brandGold,
          onSecondary: AppColors.richEspresso,
          surface: AppColors.warmIvory,
          onSurface: AppColors.charcoal,
          error: AppColors.error,
          onError: AppColors.white,
        );
    final baseTextTheme = ThemeData(
      useMaterial3: true,
      colorScheme: colorScheme,
    ).textTheme;

    return ThemeData(
      useMaterial3: true,
      colorScheme: colorScheme,
      scaffoldBackgroundColor: AppColors.warmIvory,
      textTheme: baseTextTheme.apply(
        bodyColor: AppColors.charcoal,
        displayColor: AppColors.richEspresso,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: AppColors.warmIvory,
        foregroundColor: AppColors.richEspresso,
        centerTitle: false,
        elevation: 0,
        scrolledUnderElevation: 0,
      ),
      cardTheme: CardThemeData(
        color: AppColors.white,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(24),
          side: const BorderSide(color: AppColors.softCream),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: AppColors.white,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(16),
          borderSide: const BorderSide(color: AppColors.mutedTaupe),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(16),
          borderSide: const BorderSide(color: AppColors.mutedTaupe),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(16),
          borderSide: const BorderSide(color: AppColors.primaryBrown, width: 2),
        ),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 16,
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(48, 52),
          backgroundColor: AppColors.brandGold,
          foregroundColor: AppColors.richEspresso,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16),
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: AppColors.primaryBrown,
          side: const BorderSide(color: AppColors.primaryBrown),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16),
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: AppColors.primaryBrown,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16),
          ),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: AppColors.primaryBrown,
        ),
      ),
      navigationBarTheme: const NavigationBarThemeData(
        backgroundColor: AppColors.white,
        indicatorColor: AppColors.softCream,
        elevation: 0,
        labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
      ),
      navigationRailTheme: const NavigationRailThemeData(
        backgroundColor: AppColors.warmIvory,
        indicatorColor: AppColors.softCream,
        labelType: NavigationRailLabelType.all,
      ),
    );
  }
}
