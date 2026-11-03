import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../../core/config/app_environment.dart';
import '../domain/session_repository.dart';
import 'auth_api_remote_data_source.dart';
import 'auth_response_mapper.dart';
import 'auth_session_interceptor.dart';
import 'credential_session_manager.dart';
import 'refresh_coordinator.dart';
import 'secure_token_vault.dart';
import 'session_repository_impl.dart';

final erpBaseUriProvider = Provider<Uri?>((ref) {
  try {
    return ref.watch(appEnvironmentProvider).validatedErpBaseUri;
  } on FormatException {
    return null;
  }
});

final tokenVaultProvider = Provider<TokenVault>((ref) {
  return SecureTokenVault(
    FlutterSecureStorageAdapter(
      const FlutterSecureStorage(aOptions: AndroidOptions()),
    ),
  );
});

final credentialSessionManagerProvider = Provider<CredentialSessionManager>((
  ref,
) {
  final manager = CredentialSessionManager(ref.watch(tokenVaultProvider));
  ref.onDispose(manager.dispose);
  return manager;
});

final authResponseMapperProvider = Provider<AuthResponseMapper>((ref) {
  return const AuthResponseMapper();
});

final unauthenticatedAuthDioProvider = Provider<Dio>((ref) {
  final dio = Dio(
    BaseOptions(
      baseUrl: ref.watch(erpBaseUriProvider)?.toString() ?? '',
      connectTimeout: const Duration(seconds: 15),
      receiveTimeout: const Duration(seconds: 15),
      sendTimeout: const Duration(seconds: 15),
    ),
  );
  ref.onDispose(dio.close);
  return dio;
});

final authenticatedAuthDioProvider = Provider<Dio>((ref) {
  final dio = Dio(
    BaseOptions(
      baseUrl: ref.watch(erpBaseUriProvider)?.toString() ?? '',
      connectTimeout: const Duration(seconds: 15),
      receiveTimeout: const Duration(seconds: 15),
      sendTimeout: const Duration(seconds: 15),
    ),
  );
  final credentials = ref.watch(credentialSessionManagerProvider);
  dio.interceptors.add(
    AuthSessionInterceptor(
      client: dio,
      credentials: credentials,
      refreshCoordinator: ref.watch(refreshCoordinatorProvider),
    ),
  );
  ref.onDispose(dio.close);
  return dio;
});

final authApiRemoteDataSourceProvider = Provider<AuthApiRemoteDataSource>((
  ref,
) {
  final uri = ref.watch(erpBaseUriProvider);
  final raw = ref.watch(unauthenticatedAuthDioProvider);
  final authenticated = ref.watch(authenticatedAuthDioProvider);
  return AuthApiRemoteDataSource(
    unauthenticatedDio: raw,
    authenticatedDio: authenticated,
    isConfigured: uri != null,
  );
});

final refreshCoordinatorProvider = Provider<RefreshCoordinator>((ref) {
  return RefreshCoordinator(
    unauthenticatedDio: ref.watch(unauthenticatedAuthDioProvider),
    isConfigured: ref.watch(erpBaseUriProvider) != null,
    credentials: ref.watch(credentialSessionManagerProvider),
    mapper: ref.watch(authResponseMapperProvider),
  );
});

final concreteSessionRepositoryProvider = Provider<SessionRepository>((ref) {
  return SessionRepositoryImpl(
    remote: ref.watch(authApiRemoteDataSourceProvider),
    credentials: ref.watch(credentialSessionManagerProvider),
    mapper: ref.watch(authResponseMapperProvider),
  );
});
