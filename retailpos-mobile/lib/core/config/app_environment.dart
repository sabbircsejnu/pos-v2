import 'package:flutter_riverpod/flutter_riverpod.dart';

enum AppEnvironment {
  development,
  staging;

  String? get erpBaseUrl => switch (this) {
    AppEnvironment.development => const String.fromEnvironment(
      'ERP_BASE_URL_DEVELOPMENT',
      defaultValue: 'http://10.0.2.2:5005',
    ),
    AppEnvironment.staging => const String.fromEnvironment(
      'ERP_BASE_URL_STAGING',
    ),
  };

  Uri? get validatedErpBaseUri =>
      validateErpBaseUrl(erpBaseUrl, environment: this);

  static Uri? validateErpBaseUrl(
    String? configured, {
    AppEnvironment? environment,
  }) {
    if (configured == null || configured.trim().isEmpty) {
      return null;
    }

    final uri = Uri.tryParse(configured.trim());
    final isDevelopmentEmulatorHttp =
        environment == AppEnvironment.development &&
        uri != null &&
        uri.scheme == 'http' &&
        uri.host == '10.0.2.2' &&
        uri.port == 5005 &&
        (uri.path.isEmpty || uri.path == '/') &&
        uri.userInfo.isEmpty &&
        !uri.hasQuery &&
        !uri.hasFragment;

    if (uri == null ||
        (uri.scheme != 'https' && !isDevelopmentEmulatorHttp) ||
        uri.host.isEmpty ||
        uri.userInfo.isNotEmpty ||
        uri.hasQuery ||
        uri.hasFragment) {
      throw FormatException(
        'ERP base URL is invalid for the selected environment.',
      );
    }

    return uri.replace(
      path: uri.path.endsWith('/') ? uri.path : '${uri.path}/',
    );
  }

  static AppEnvironment fromDartDefine() {
    const value = String.fromEnvironment(
      'APP_ENV',
      defaultValue: 'development',
    );
    return fromValue(value);
  }

  static AppEnvironment fromValue(String value) => switch (value) {
    'development' => AppEnvironment.development,
    'staging' => AppEnvironment.staging,
    _ => throw FormatException('Unsupported APP_ENV value: $value'),
  };
}

final appEnvironmentProvider = Provider<AppEnvironment>((ref) {
  return AppEnvironment.fromDartDefine();
});
