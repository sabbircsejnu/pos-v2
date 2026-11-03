import 'package:iranimart_business_mobile/core/config/app_environment.dart';
import 'package:iranimart_business_mobile/features/foundation/domain/foundation_status.dart';
import 'package:iranimart_business_mobile/features/foundation/domain/foundation_status_repository.dart';

final class ConfiguredFoundationStatusRepository
    implements FoundationStatusRepository {
  const ConfiguredFoundationStatusRepository(this._environment);

  final AppEnvironment _environment;

  @override
  FoundationStatus read() {
    return FoundationStatus(
      environment: _environment.name,
      enabledCapabilities: const {},
    );
  }
}
