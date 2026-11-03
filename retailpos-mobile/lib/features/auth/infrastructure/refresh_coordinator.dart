import 'package:dio/dio.dart';

import '../domain/auth_failure.dart';
import '../domain/auth_models.dart';
import 'auth_api_remote_data_source.dart';
import 'auth_response_mapper.dart';
import 'credential_session_manager.dart';

final class RefreshCoordinator {
  RefreshCoordinator({
    required this._unauthenticatedDio,
    required this._isConfigured,
    required this._credentials,
    required this._mapper,
  });

  final Dio _unauthenticatedDio;
  final bool _isConfigured;
  final CredentialSessionManager _credentials;
  final AuthResponseMapper _mapper;
  final Map<int, Future<TokenPair>> _inFlight = {};

  Future<TokenPair> refresh(int generation, TokenPair previous) {
    final existing = _inFlight[generation];
    if (existing != null) return existing;

    late final Future<TokenPair> operation;
    operation = _perform(generation, previous).whenComplete(() {
      if (identical(_inFlight[generation], operation)) {
        _inFlight.remove(generation);
      }
    });
    _inFlight[generation] = operation;
    return operation;
  }

  Future<TokenPair> _perform(int generation, TokenPair previous) async {
    final stored = await _credentials.read(generation);
    if (!_credentials.isCurrent(generation) ||
        stored == null ||
        !stored.hasSameSecrets(previous)) {
      throw const AuthFailure(AuthFailureKind.sessionEnded);
    }
    try {
      if (!_isConfigured) {
        throw const AuthFailure(AuthFailureKind.configuration);
      }
      final raw = (await _unauthenticatedDio.post<Object?>(
        'api/auth/refresh-token',
        data: {
          'token': previous.accessToken,
          'refreshToken': previous.refreshToken,
        },
      )).data;
      if (raw is! Map) {
        throw const AuthFailure(AuthFailureKind.invalidServerResponse);
      }
      final response = Map<String, dynamic>.from(raw);
      final replacement = _mapper.tokenPair(response);
      if (!_credentials.isCurrent(generation) ||
          !await _credentials.replace(generation, replacement)) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }
      return replacement;
    } on DioException catch (error) {
      await _credentials.clear(generation);
      final failure = AuthApiRemoteDataSource.failureFromDio(error);
      if (failure.kind == AuthFailureKind.offline) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }
      throw failure;
    } on AuthFailure {
      await _credentials.clear(generation);
      rethrow;
    }
  }
}
