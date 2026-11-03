import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/config/app_environment.dart';

void main() {
  group('AppEnvironment', () {
    test(
      'ERP base URL allows HTTPS and only the development emulator HTTP URL',
      () {
        expect(
          AppEnvironment.validateErpBaseUrl('https://erp.example.test/api'),
          Uri.parse('https://erp.example.test/api/'),
        );
        expect(
          AppEnvironment.validateErpBaseUrl(
            'http://10.0.2.2:5005',
            environment: AppEnvironment.development,
          ),
          Uri.parse('http://10.0.2.2:5005/'),
        );
        expect(AppEnvironment.validateErpBaseUrl(null), isNull);
        expect(AppEnvironment.validateErpBaseUrl('  '), isNull);
        expect(
          () => AppEnvironment.validateErpBaseUrl('http://erp.example.test/'),
          throwsFormatException,
        );
        expect(
          () => AppEnvironment.validateErpBaseUrl(
            'http://10.0.2.2:5005',
            environment: AppEnvironment.staging,
          ),
          throwsFormatException,
        );
        expect(
          () => AppEnvironment.validateErpBaseUrl(
            'http://10.0.2.2:5006',
            environment: AppEnvironment.development,
          ),
          throwsFormatException,
        );
        expect(
          () => AppEnvironment.validateErpBaseUrl(
            'http://10.0.2.2:5005/api',
            environment: AppEnvironment.development,
          ),
          throwsFormatException,
        );
        expect(
          () => AppEnvironment.validateErpBaseUrl(
            'https://user:password@erp.example.test/',
          ),
          throwsFormatException,
        );
        expect(
          () => AppEnvironment.validateErpBaseUrl(
            'https://erp.example.test/?tenant=8',
          ),
          throwsFormatException,
        );
      },
    );

    test('parses development and staging values', () {
      expect(
        AppEnvironment.fromValue('development'),
        AppEnvironment.development,
      );
      expect(AppEnvironment.fromValue('staging'), AppEnvironment.staging);
    });

    test('rejects unsupported values instead of silently defaulting', () {
      expect(
        () => AppEnvironment.fromValue('production'),
        throwsFormatException,
      );
    });
  });
}
