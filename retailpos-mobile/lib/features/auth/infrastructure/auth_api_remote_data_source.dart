import 'package:dio/dio.dart';

import '../domain/auth_failure.dart';
import '../domain/auth_models.dart';

final class AuthApiRemoteDataSource {
  AuthApiRemoteDataSource({
    required this._unauthenticatedDio,
    required this._authenticatedDio,
    required this._isConfigured,
  });

  final Dio _unauthenticatedDio;
  final Dio _authenticatedDio;
  final bool _isConfigured;

  Future<Map<String, dynamic>> login({
    required String email,
    required String password,
  }) async {
    _ensureConfigured();
    try {
      return _object(
        (await _unauthenticatedDio.post<Object?>(
          'api/auth/login',
          data: {'email': email, 'password': password, 'rememberMe': false},
        )).data,
      );
    } on DioException catch (error) {
      throw failureFromDio(error, login: true);
    }
  }

  Future<Map<String, dynamic>> currentUser() async {
    _ensureConfigured();
    try {
      return _object(
        (await _authenticatedDio.get<Object?>('api/auth/me')).data,
      );
    } on DioException catch (error) {
      throw failureFromDio(error);
    }
  }

  Future<Map<String, dynamic>> authorizedOutlets() async {
    _ensureConfigured();
    try {
      final response = _object(
        (await _authenticatedDio.get<Object?>('api/me/outlets')).data,
      );
      return _object(response['data']);
    } on DioException catch (error) {
      throw failureFromDio(error);
    }
  }

  Future<void> logout(TokenPair pair) async {
    _ensureConfigured();
    try {
      await _unauthenticatedDio.post<Object?>(
        'api/auth/logout',
        data: {'refreshToken': pair.refreshToken},
        options: Options(
          headers: {'Authorization': 'Bearer ${pair.accessToken}'},
        ),
      );
    } on DioException catch (error) {
      throw failureFromDio(error);
    }
  }

  void _ensureConfigured() {
    if (!_isConfigured) {
      throw const AuthFailure(AuthFailureKind.configuration);
    }
  }

  static Map<String, dynamic> _object(Object? value) {
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return Map<String, dynamic>.from(value);
    throw const AuthFailure(AuthFailureKind.invalidServerResponse);
  }

  static AuthFailure failureFromDio(DioException error, {bool login = false}) {
    final status = error.response?.statusCode;
    final serverMessage = _safeMessage(error.response?.data)?.toLowerCase();
    if (serverMessage?.contains('password setup is required') == true ||
        serverMessage?.contains('complete your invitation') == true) {
      return const AuthFailure(AuthFailureKind.passwordSetupRequired);
    }
    if (status == 401) {
      return AuthFailure(
        login
            ? AuthFailureKind.invalidCredentials
            : AuthFailureKind.sessionEnded,
      );
    }
    if (status == 403) {
      return const AuthFailure(AuthFailureKind.forbidden);
    }
    if (status == 429) {
      return const AuthFailure(AuthFailureKind.rateLimited);
    }
    if (status != null && status >= 500) {
      return const AuthFailure(AuthFailureKind.server);
    }
    if (error.type == DioExceptionType.connectionTimeout ||
        error.type == DioExceptionType.sendTimeout ||
        error.type == DioExceptionType.receiveTimeout ||
        error.type == DioExceptionType.connectionError) {
      return const AuthFailure(AuthFailureKind.offline);
    }
    if (error.error is AuthFailure) {
      return error.error! as AuthFailure;
    }
    return const AuthFailure(AuthFailureKind.server);
  }

  static String? _safeMessage(Object? data) {
    if (data is Map) {
      final message = data['message'];
      if (message is String) return message;
    }
    return null;
  }
}
