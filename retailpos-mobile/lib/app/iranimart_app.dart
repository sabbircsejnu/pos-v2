import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iranimart_business_mobile/app/router/app_router.dart';
import 'package:iranimart_business_mobile/shared/design_system/app_theme.dart';

class IraniMartApp extends ConsumerWidget {
  const IraniMartApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      title: 'IraniMart Business',
      theme: AppTheme.light,
      routerConfig: ref.watch(appRouterProvider),
    );
  }
}
