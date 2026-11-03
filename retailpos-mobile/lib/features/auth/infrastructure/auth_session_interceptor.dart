import 'package:dio/dio.dart';

import '../domain/auth_failure.dart';
import 'credential_session_manager.dart';
import 'refresh_coordinator.dart';

final class AuthSessionInterceptor extends Interceptor {
  AuthSessionInterceptor({
    required this._client,
    required this._credentials,
    required this._refreshCoordinator,
  });

  static const _generationKey = 'authGeneration';
  static const _retriedKey = 'authRetried';

  final Dio _client;
  final CredentialSessionManager _credentials;
  final RefreshCoordinator _refreshCoordinator;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final generation = _credentials.generation;
    try {
      final pair = await _credentials.read(generation);
      if (!_credentials.isCurrent(generation) || pair == null) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }
      options.extra[_generationKey] = generation;
      options.headers['Authorization'] = 'Bearer ${pair.accessToken}';
      options.cancelToken ??= _credentials.cancelToken;
      handler.next(options);
    } on AuthFailure catch (failure) {
      handler.reject(
        DioException(
          requestOptions: options,
          error: failure,
          type: DioExceptionType.cancel,
        ),
      );
    }
  }

  @override
  void onResponse(
    Response<dynamic> response,
    ResponseInterceptorHandler handler,
  ) {
    final generation = response.requestOptions.extra[_generationKey];
    if (generation is int && !_credentials.isCurrent(generation)) {
      handler.reject(
        DioException(
          requestOptions: response.requestOptions,
          error: const AuthFailure(AuthFailureKind.sessionEnded),
          type: DioExceptionType.cancel,
        ),
      );
      return;
    }
    handler.next(response);
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final options = err.requestOptions;
    final generation = options.extra[_generationKey];
    if (err.response?.statusCode == 401 &&
        generation is int &&
        options.extra[_retriedKey] == true &&
        _credentials.isCurrent(generation)) {
      try {
        await _credentials.clear(generation);
        handler.reject(_sessionEnded(options));
      } on AuthFailure catch (failure) {
        handler.reject(_sessionEnded(options, failure));
      }
      return;
    }
    if (err.response?.statusCode != 401 ||
        generation is! int ||
        options.extra[_retriedKey] == true ||
        (options.method != 'GET' && options.method != 'HEAD') ||
        !_credentials.isCurrent(generation)) {
      handler.next(err);
      return;
    }

    try {
      final previous = await _credentials.read(generation);
      if (previous == null) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }
      final requestToken = _bearer(options.headers['Authorization']);
      if (requestToken == null) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }

      if (requestToken == previous.accessToken) {
        await _refreshCoordinator.refresh(generation, previous);
      }
      if (!_credentials.isCurrent(generation)) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }

      options.extra[_retriedKey] = true;
      final response = await _client.fetch<dynamic>(options);
      handler.resolve(response);
    } on AuthFailure catch (failure) {
      handler.reject(_sessionEnded(options, failure));
    } on DioException catch (replayError) {
      handler.next(replayError);
    }
  }

  static DioException _sessionEnded(
    RequestOptions options, [
    AuthFailure failure = const AuthFailure(AuthFailureKind.sessionEnded),
  ]) => DioException(
    requestOptions: options,
    error: failure,
    type: DioExceptionType.unknown,
  );

  static String? _bearer(Object? value) {
    if (value is! String || !value.startsWith('Bearer ')) return null;
    return value.substring('Bearer '.length);
  }
}
