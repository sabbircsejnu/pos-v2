import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

const _packagePrefix = 'package:iranimart_business_mobile/features/';
const _allowedTargetsByLayer = <String, Set<String>>{
  'domain': {'domain'},
  'application': {'domain', 'application'},
  'infrastructure': {'domain', 'application', 'infrastructure'},
  'presentation': {'domain', 'application', 'presentation'},
};

void main() {
  test('feature imports respect Clean Architecture layer boundaries', () {
    final featureRoot = Directory('lib/features');
    expect(featureRoot.existsSync(), isTrue);

    for (final source
        in featureRoot.listSync(recursive: true).whereType<File>()) {
      if (!source.path.endsWith('.dart')) {
        continue;
      }

      final sourceParts = source.uri.pathSegments;
      final sourceFeatureIndex = sourceParts.lastIndexOf('features');
      if (sourceFeatureIndex < 0 ||
          sourceParts.length <= sourceFeatureIndex + 2) {
        continue;
      }

      final sourceFeature = sourceParts[sourceFeatureIndex + 1];
      final sourceLayer = sourceParts[sourceFeatureIndex + 2];
      final allowedTargets = _allowedTargetsByLayer[sourceLayer];
      if (allowedTargets == null) {
        continue;
      }

      for (final match in RegExp(
        "import\\s+'$_packagePrefix([^/]+)/([^/]+)/",
      ).allMatches(source.readAsStringSync())) {
        final targetFeature = match.group(1)!;
        final targetLayer = match.group(2)!;

        expect(
          targetFeature,
          sourceFeature,
          reason: '${source.path} must not depend on another feature directly.',
        );
        expect(
          allowedTargets,
          contains(targetLayer),
          reason: '${source.path} cannot depend on the $targetLayer layer.',
        );
      }
    }
  });

  test(
    'foundation and auth features have all four architecture boundaries',
    () {
      for (final layer in _allowedTargetsByLayer.keys) {
        expect(
          Directory('lib/features/foundation/$layer').existsSync(),
          isTrue,
          reason: 'Missing foundation $layer boundary.',
        );
        expect(
          Directory('lib/features/auth/$layer').existsSync(),
          isTrue,
          reason: 'Missing auth $layer boundary.',
        );
      }
    },
  );
}
