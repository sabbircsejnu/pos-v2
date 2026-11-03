import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_failure.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_api_remote_data_source.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_response_mapper.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_session_interceptor.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/credential_session_manager.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/refresh_coordinator.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/session_repository_impl.dart';

import 'auth_test_fakes.dart';

void main() {
  test('login preserves ERP DTO shape and retrieves authoritative profile and outlets', () async {
    final calls = <RequestOptions>[];
    final harness = _harness((request) async {
      calls.add(request);
      return switch (request.uri.path) {
        '/api/auth/login' => jsonResponse(_loginResponse(), 200),
        '/api/auth/me' => jsonResponse(_profile(), 200),
        '/api/me/outlets' => jsonResponse(_outlets(), 200),
        _ => jsonResponse({'message': 'not found'}, 404),
      };
    });

    final session = await harness.repository.signIn(
      email: 'owner@example.test',
      password: 'sample password',
    );

    expect(session.profile.businessId, 8);
    expect(session.outlets.map((outlet) => outlet.id), [21, 22]);
    expect(session.selectedOutletId, 21);
    expect(calls.map((request) => request.uri.path), [
      '/api/auth/login',
      '/api/auth/me',
      '/api/me/outlets',
    ]);
    expect(calls.first.data, {
      'email': 'owner@example.test',
      'password': 'sample password',
      'rememberMe': false,
    });
    expect(calls[1].headers['Authorization'], 'Bearer access-one');
    expect(calls[2].headers['Authorization'], 'Bearer access-one');
    expect(
      calls.any(
        (request) => request.headers.keys.any(
          (key) => key.toLowerCase().contains('business'),
        ),
      ),
      isFalse,
    );
    expect(harness.vault.pair?.refreshToken, 'refresh-one');
  });

  test('invalid credentials do not persist a session', () async {
    final harness = _harness((request) async {
      return jsonResponse({'message': 'Invalid email or password'}, 401);
    });

    await expectLater(
      harness.repository.signIn(
        email: 'owner@example.test',
        password: 'wrong password',
      ),
      throwsA(
        isA<AuthFailure>().having(
          (failure) => failure.kind,
          'kind',
          AuthFailureKind.invalidCredentials,
        ),
      ),
    );
    expect(harness.vault.pair, isNull);
  });

  test(
    'password setup requirement stays distinct from invalid credentials',
    () async {
      final harness = _harness((request) async {
        return jsonResponse({
          'message': 'Password setup is required. Complete your invitation.',
        }, 401);
      });

      await expectLater(
        harness.repository.signIn(
          email: 'owner@example.test',
          password: 'sample password',
        ),
        throwsA(
          isA<AuthFailure>().having(
            (failure) => failure.kind,
            'kind',
            AuthFailureKind.passwordSetupRequired,
          ),
        ),
      );
    },
  );

  test(
    'rate limiting and network failure have distinct typed states',
    () async {
      final rateLimited = _harness((request) async {
        return jsonResponse({'message': 'Too many requests'}, 429);
      });
      await expectLater(
        rateLimited.repository.signIn(
          email: 'owner@example.test',
          password: 'sample password',
        ),
        throwsA(
          isA<AuthFailure>().having(
            (failure) => failure.kind,
            'kind',
            AuthFailureKind.rateLimited,
          ),
        ),
      );

      final offline = _harness((request) async {
        throw DioException(
          requestOptions: request,
          type: DioExceptionType.connectionError,
        );
      });
      await expectLater(
        offline.repository.signIn(
          email: 'owner@example.test',
          password: 'sample password',
        ),
        throwsA(
          isA<AuthFailure>().having(
            (failure) => failure.kind,
            'kind',
            AuthFailureKind.offline,
          ),
        ),
      );
      expect(
        const AuthFailure(AuthFailureKind.invalidCredentials).toString(),
        isNot(contains('sample password')),
      );
    },
  );

  test('session restoration refetches profile and outlet grants', () async {
    final calls = <String>[];
    final harness = _harness((request) async {
      calls.add(request.uri.path);
      return switch (request.uri.path) {
        '/api/auth/me' => jsonResponse(_profile(), 200),
        '/api/me/outlets' => jsonResponse(_outlets(), 200),
        _ => jsonResponse({'message': 'Unexpected request'}, 404),
      };
    });
    final generation = harness.credentials.beginSession();
    await harness.credentials.replace(generation, samplePair);

    final session = await harness.repository.restoreSession();

    expect(calls, ['/api/auth/me', '/api/me/outlets']);
    expect(session?.profile.businessId, 8);
    expect(session?.outlets.length, 2);
  });

  test(
    'logout always clears local credentials when server revocation fails',
    () async {
      final harness = _harness((request) async {
        expect(request.uri.path, '/api/auth/logout');
        expect(request.headers['Authorization'], 'Bearer access-one');
        expect(request.data, {'refreshToken': 'refresh-one'});
        return jsonResponse({'message': 'Unauthorized'}, 401);
      });
      final generation = harness.credentials.beginSession();
      await harness.credentials.replace(generation, samplePair);

      final result = await harness.repository.signOut();

      expect(result.remoteRevocationConfirmed, isFalse);
      expect(harness.vault.pair, isNull);
    },
  );

  test('successful server logout is reported as confirmed', () async {
    final harness = _harness((request) async {
      return jsonResponse({'message': 'Logged out successfully'}, 200);
    });
    final generation = harness.credentials.beginSession();
    await harness.credentials.replace(generation, samplePair);

    final result = await harness.repository.signOut();

    expect(result.remoteRevocationConfirmed, isTrue);
    expect(harness.vault.pair, isNull);
  });
}

_AuthHarness _harness(
  Future<ResponseBody> Function(RequestOptions request) handle,
) {
  final vault = MemoryTokenVault();
  final credentials = CredentialSessionManager(vault);
  final mapper = const AuthResponseMapper();
  final raw = Dio(BaseOptions(baseUrl: 'https://erp.example.test/'));
  final authenticated = Dio(BaseOptions(baseUrl: 'https://erp.example.test/'));
  final adapter = CallbackHttpAdapter(handle);
  raw.httpClientAdapter = adapter;
  authenticated.httpClientAdapter = adapter;
  final refresh = RefreshCoordinator(
    unauthenticatedDio: raw,
    isConfigured: true,
    credentials: credentials,
    mapper: mapper,
  );
  authenticated.interceptors.add(
    AuthSessionInterceptor(
      client: authenticated,
      credentials: credentials,
      refreshCoordinator: refresh,
    ),
  );
  final remote = AuthApiRemoteDataSource(
    unauthenticatedDio: raw,
    authenticatedDio: authenticated,
    isConfigured: true,
  );
  return _AuthHarness(
    vault: vault,
    credentials: credentials,
    repository: SessionRepositoryImpl(
      remote: remote,
      credentials: credentials,
      mapper: mapper,
    ),
  );
}

final class _AuthHarness {
  const _AuthHarness({
    required this.vault,
    required this.credentials,
    required this.repository,
  });

  final MemoryTokenVault vault;
  final CredentialSessionManager credentials;
  final SessionRepositoryImpl repository;
}

Map<String, dynamic> _loginResponse() => {
  'token': 'access-one',
  'refreshToken': 'refresh-one',
  'expiresAt': '2030-01-01T00:00:00Z',
  'user': {'id': 4},
};

Map<String, dynamic> _profile() => {
  'id': 4,
  'businessId': 8,
  'name': 'A User',
  'email': 'owner@example.test',
  'roleName': 'Business Owner',
  'isRoleSwitched': false,
  'isBusinessOwner': true,
  'mustResetPassword': false,
};

Map<String, dynamic> _outlets() => {
  'data': {
    'outlets': [
      {'id': 21, 'name': 'North', 'type': 'outlet'},
      {'id': 22, 'name': 'South', 'type': 'outlet'},
    ],
    'warehouses': [
      {'id': 30, 'name': 'Warehouse', 'type': 'warehouse'},
    ],
    'defaultOutletId': 21,
  },
};
