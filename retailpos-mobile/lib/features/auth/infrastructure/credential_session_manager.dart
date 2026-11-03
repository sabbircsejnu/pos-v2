import 'dart:async';

import 'package:dio/dio.dart';

import '../domain/auth_models.dart';
import 'secure_token_vault.dart';

final class CredentialSessionManager {
  CredentialSessionManager(this._vault);

  final TokenVault _vault;
  int _generation = 0;
  int? _pairGeneration;
  Future<void> _storageQueue = Future<void>.value();
  CancelToken _cancelToken = CancelToken();

  int get generation => _generation;
  CancelToken get cancelToken => _cancelToken;

  int beginSession() {
    _generation++;
    _pairGeneration = null;
    _cancelToken.cancel('Authentication session changed.');
    _cancelToken = CancelToken();
    return _generation;
  }

  bool isCurrent(int generation) => generation == _generation;

  Future<TokenPair?> restore(int generation) => _serialized(() async {
    if (!isCurrent(generation)) return null;
    final pair = await _vault.read();
    if (!isCurrent(generation)) return null;
    if (pair == null) return null;
    _pairGeneration = generation;
    return pair;
  });

  Future<TokenPair?> read(int generation) => _serialized(() async {
    if (!isCurrent(generation) || _pairGeneration != generation) return null;
    return _vault.read();
  });

  Future<bool> replace(int generation, TokenPair pair) => _serialized(() async {
    if (!isCurrent(generation)) return false;
    await _vault.replace(pair);
    if (!isCurrent(generation)) {
      await _vault.clear();
      _pairGeneration = null;
      return false;
    }
    _pairGeneration = generation;
    return true;
  });

  Future<void> clear(int generation) => _serialized(() async {
    if (!isCurrent(generation)) return;
    await _vault.clear();
    _pairGeneration = null;
  });

  Future<T> _serialized<T>(Future<T> Function() action) async {
    final previous = _storageQueue;
    final release = Completer<void>();
    _storageQueue = release.future;
    await previous;
    try {
      return await action();
    } finally {
      release.complete();
    }
  }

  void dispose() {
    _cancelToken.cancel('Authentication provider disposed.');
  }
}
