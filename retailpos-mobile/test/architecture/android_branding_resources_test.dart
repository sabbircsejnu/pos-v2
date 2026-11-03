import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';

const _androidRes = 'android/app/src/main/res';
const _launcherDensitySizes = <String, int>{
  'mdpi': 48,
  'hdpi': 72,
  'xhdpi': 96,
  'xxhdpi': 144,
  'xxxhdpi': 192,
};
const _adaptiveDensitySizes = <String, int>{
  'mdpi': 108,
  'hdpi': 162,
  'xhdpi': 216,
  'xxhdpi': 324,
  'xxxhdpi': 432,
};

void main() {
  test('launcher manifest uses legacy and adaptive IraniMart icons', () {
    final manifest = File('android/app/src/main/AndroidManifest.xml')
        .readAsStringSync();
    expect(manifest, contains('android:icon="@mipmap/ic_launcher"'));
    expect(manifest, contains('android:roundIcon="@mipmap/ic_launcher_round"'));

    for (final name in ['ic_launcher.xml', 'ic_launcher_round.xml']) {
      final adaptiveIcon = File('$_androidRes/mipmap-anydpi-v26/$name')
          .readAsStringSync();
      expect(adaptiveIcon, contains('@drawable/iranimart_launcher_foreground'));
      expect(adaptiveIcon, contains('@color/iranimart_launcher_background'));
    }
  });

  test('legacy and adaptive launcher images have density-correct dimensions', () {
    for (final entry in _launcherDensitySizes.entries) {
      for (final filename in ['ic_launcher.png', 'ic_launcher_round.png']) {
        final bytes = File('$_androidRes/mipmap-${entry.key}/$filename')
            .readAsBytesSync();
        expect(_pngDimensions(bytes), (entry.value, entry.value));
      }
    }

    for (final entry in _adaptiveDensitySizes.entries) {
      final bytes = File(
        '$_androidRes/drawable-${entry.key}/iranimart_launcher_foreground.png',
      ).readAsBytesSync();
      expect(_pngDimensions(bytes), (entry.value, entry.value));
    }
  });

  test('native splash uses supplied logo and Android 12 mark', () {
    for (final path in [
      '$_androidRes/drawable/launch_background.xml',
      '$_androidRes/drawable-v21/launch_background.xml',
    ]) {
      expect(
        File(path).readAsStringSync(),
        contains('@drawable/iranimart_splash_logo'),
      );
    }

    for (final path in [
      '$_androidRes/values-v31/styles.xml',
      '$_androidRes/values-night-v31/styles.xml',
    ]) {
      final styles = File(path).readAsStringSync();
      expect(styles, contains('@drawable/iranimart_splash_mark'));
      expect(styles, contains('@color/iranimart_warm_ivory'));
    }

    for (final density in _launcherDensitySizes.keys) {
      expect(
        File('$_androidRes/drawable-$density/iranimart_splash_logo.png')
            .existsSync(),
        isTrue,
      );
    }
    expect(
      File('$_androidRes/drawable-nodpi/iranimart_splash_mark.png')
          .existsSync(),
      isTrue,
    );
  });
}

(int, int) _pngDimensions(Uint8List bytes) {
  expect(bytes.sublist(0, 8), [137, 80, 78, 71, 13, 10, 26, 10]);
  final data = ByteData.sublistView(bytes);
  return (data.getUint32(16), data.getUint32(20));
}
