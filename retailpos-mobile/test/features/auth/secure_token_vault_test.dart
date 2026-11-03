import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_failure.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/secure_token_vault.dart';

import 'auth_test_fakes.dart';

void main() {
  test(
    'stores and restores the complete token pair from one secure record',
    () async {
      final storage = MemorySecureStorage();
      final vault = SecureTokenVault(storage);
      await vault.replace(samplePair);

      final restored = await vault.read();

      expect(restored?.accessToken, samplePair.accessToken);
      expect(restored?.refreshToken, samplePair.refreshToken);
      expect(restored?.expiresAt, samplePair.expiresAt);
      expect(storage.value, contains('"version":1'));
      expect(vault.toString(), isNot(contains(samplePair.accessToken)));
    },
  );

  test('a truncated persisted record is cleared and rejected', () async {
    final storage = MemorySecureStorage()
      ..value = '{"version":1,"accessToken":"partial';
    final vault = SecureTokenVault(storage);

    await expectLater(
      vault.read(),
      throwsA(
        isA<AuthFailure>().having(
          (failure) => failure.kind,
          'kind',
          AuthFailureKind.secureStorage,
        ),
      ),
    );
    expect(storage.value, isNull);
  });

  test(
    'an interrupted replacement never exposes a partial token pair',
    () async {
      final storage = MemorySecureStorage()..writePartialThenFail = true;
      final vault = SecureTokenVault(storage);

      await expectLater(vault.replace(samplePair), throwsA(isA<AuthFailure>()));
      await expectLater(vault.read(), throwsA(isA<AuthFailure>()));
      expect(storage.value, isNull);
    },
  );

  test(
    'storage failures remain typed and do not fall back to another store',
    () async {
      final storage = MemorySecureStorage()..failRead = true;
      final vault = SecureTokenVault(storage);

      await expectLater(
        vault.read(),
        throwsA(
          isA<AuthFailure>().having(
            (failure) => failure.kind,
            'kind',
            AuthFailureKind.secureStorage,
          ),
        ),
      );
    },
  );
}
