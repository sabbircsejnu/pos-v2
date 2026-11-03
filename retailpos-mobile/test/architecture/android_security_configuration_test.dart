import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'Android backups exclude credentials and network access is declared',
    () {
      final manifest = File('android/app/src/main/AndroidManifest.xml')
          .readAsStringSync();
      final legacyRules = File('android/app/src/main/res/xml/backup_rules.xml')
          .readAsStringSync();
      final extractionRules = File(
        'android/app/src/main/res/xml/data_extraction_rules.xml',
      ).readAsStringSync();
      final developmentManifest = File(
        'android/app/src/development/AndroidManifest.xml',
      ).readAsStringSync();
      final developmentNetworkConfig = File(
        'android/app/src/development/res/xml/network_security_config.xml',
      ).readAsStringSync();

      expect(manifest, contains('android:allowBackup="false"'));
      expect(
        manifest,
        contains('android:fullBackupContent="@xml/backup_rules"'),
      );
      expect(
        manifest,
        contains('android:dataExtractionRules="@xml/data_extraction_rules"'),
      );
      expect(manifest, contains('android.permission.INTERNET'));
      expect(manifest, isNot(contains('android:usesCleartextTraffic="true"')));
      expect(
        developmentManifest,
        contains(
          'android:networkSecurityConfig="@xml/network_security_config"',
        ),
      );
      expect(
        developmentNetworkConfig,
        contains('cleartextTrafficPermitted="false"'),
      );
      expect(
        developmentNetworkConfig,
        contains('<domain includeSubdomains="false">10.0.2.2</domain>'),
      );
      expect(
        developmentNetworkConfig,
        contains('<domain-config cleartextTrafficPermitted="true">'),
      );
      expect(
        File('android/app/src/staging/AndroidManifest.xml').existsSync(),
        isFalse,
      );
      expect(legacyRules, contains('domain="sharedpref" path="."'));
      expect(extractionRules, contains('<cloud-backup>'));
      expect(extractionRules, contains('<device-transfer>'));
      expect(extractionRules, contains('domain="sharedpref" path="."'));
    },
  );
}
