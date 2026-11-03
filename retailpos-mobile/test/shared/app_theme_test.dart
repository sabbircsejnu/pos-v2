import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/shared/design_system/app_colors.dart';
import 'package:iranimart_business_mobile/shared/design_system/app_theme.dart';

void main() {
  test('enabled text field outline has accessible contrast on white', () {
    final border =
        AppTheme.light.inputDecorationTheme.enabledBorder!
            as OutlineInputBorder;
    final color = border.borderSide.color;

    expect(color, AppColors.mutedTaupe);
    expect(_contrastRatio(color, AppColors.white), greaterThanOrEqualTo(3));
  });

  test('primary action text has accessible contrast on the gold surface', () {
    final style = AppTheme.light.filledButtonTheme.style!;
    final background = style.backgroundColor!.resolve(const <WidgetState>{})!;
    final foreground = style.foregroundColor!.resolve(const <WidgetState>{})!;

    expect(background, AppColors.brandGold);
    expect(foreground, AppColors.richEspresso);
    expect(_contrastRatio(foreground, background), greaterThanOrEqualTo(4.5));
  });

  test('shared button themes provide 48 logical-pixel hit targets', () {
    final theme = AppTheme.light;
    final minimumSizes = [
      theme.filledButtonTheme.style!.minimumSize!.resolve(
        const <WidgetState>{},
      )!,
      theme.outlinedButtonTheme.style!.minimumSize!.resolve(
        const <WidgetState>{},
      )!,
      theme.textButtonTheme.style!.minimumSize!.resolve(const <WidgetState>{})!,
      theme.iconButtonTheme.style!.minimumSize!.resolve(const <WidgetState>{})!,
    ];

    for (final size in minimumSizes) {
      expect(size.width, greaterThanOrEqualTo(48));
      expect(size.height, greaterThanOrEqualTo(48));
    }
  });
}

double _contrastRatio(Color first, Color second) {
  final luminances = [first.computeLuminance(), second.computeLuminance()]
    ..sort((a, b) => b.compareTo(a));
  return (luminances[0] + 0.05) / (luminances[1] + 0.05);
}
