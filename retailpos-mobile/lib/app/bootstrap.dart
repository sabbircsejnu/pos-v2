import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iranimart_business_mobile/app/iranimart_app.dart';
import 'package:iranimart_business_mobile/core/config/app_environment.dart';
import 'package:iranimart_business_mobile/core/logging/app_logger.dart';
import 'package:iranimart_business_mobile/features/foundation/application/foundation_status_provider.dart';
import 'package:iranimart_business_mobile/features/foundation/infrastructure/configured_foundation_status_repository.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_providers.dart';

Future<void> bootstrap() async {
  WidgetsFlutterBinding.ensureInitialized();

  const logger = DebugAppLogger();
  logger.record(AppLogEvent.appStarted);

  runApp(
    ProviderScope(
      overrides: [
        appEnvironmentProvider.overrideWithValue(
          AppEnvironment.fromDartDefine(),
        ),
        appLoggerProvider.overrideWithValue(logger),
        foundationStatusRepositoryProvider.overrideWith(
          (ref) => ConfiguredFoundationStatusRepository(
            ref.watch(appEnvironmentProvider),
          ),
        ),
        sessionRepositoryProvider.overrideWith(
          (ref) => ref.watch(concreteSessionRepositoryProvider),
        ),
      ],
      child: const IraniMartApp(),
    ),
  );
}
