import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../domain/auth_failure.dart';
import '../domain/auth_models.dart';

abstract interface class SecureStorageAdapter {
  Future<String?> read({required String key});

  Future<void> write({required String key, required String value});

  Future<void> delete({required String key});
}

final class FlutterSecureStorageAdapter implements SecureStorageAdapter {
  FlutterSecureStorageAdapter(this._storage);

  final FlutterSecureStorage _storage;

  @override
  Future<String?> read({required String key}) => _storage.read(key: key);

  @override
  Future<void> write({required String key, required String value}) =>
      _storage.write(key: key, value: value);

  @override
  Future<void> delete({required String key}) => _storage.delete(key: key);
}

abstract interface class TokenVault {
  Future<TokenPair?> read();

  Future<void> replace(TokenPair pair);

  Future<void> clear();
}

final class SecureTokenVault implements TokenVault {
  SecureTokenVault(this._storage);

  static const _key = 'mobile.auth.token-pair.v1';
  final SecureStorageAdapter _storage;

  @override
  Future<TokenPair?> read() async {
    try {
      final value = await _storage.read(key: _key);
      if (value == null) return null;
      final decoded = jsonDecode(value);
      if (decoded is! Map<String, dynamic> ||
          decoded['version'] != 1 ||
          decoded['accessToken'] is! String ||
          decoded['refreshToken'] is! String ||
          decoded['expiresAt'] is! String) {
        await clear();
        throw const AuthFailure(AuthFailureKind.secureStorage);
      }
      final pair = TokenPair(
        accessToken: decoded['accessToken'] as String,
        refreshToken: decoded['refreshToken'] as String,
        expiresAt: DateTime.parse(decoded['expiresAt'] as String).toUtc(),
      );
      if (!pair.isUsable) {
        await clear();
        throw const AuthFailure(AuthFailureKind.secureStorage);
      }
      return pair;
    } on AuthFailure {
      rethrow;
    } on FormatException {
      await _clearAfterCorruption();
      throw const AuthFailure(AuthFailureKind.secureStorage);
    } catch (_) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
  }

  @override
  Future<void> replace(TokenPair pair) async {
    if (!pair.isUsable) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    try {
      await _storage.write(
        key: _key,
        value: jsonEncode({
          'version': 1,
          'accessToken': pair.accessToken,
          'refreshToken': pair.refreshToken,
          'expiresAt': pair.expiresAt.toUtc().toIso8601String(),
        }),
      );
    } catch (_) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
  }

  @override
  Future<void> clear() async {
    try {
      await _storage.delete(key: _key);
    } catch (_) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
  }

  Future<void> _clearAfterCorruption() async {
    try {
      await _storage.delete(key: _key);
    } catch (_) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
  }
}
