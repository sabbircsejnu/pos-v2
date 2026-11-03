import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_failure.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_response_mapper.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_session_interceptor.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/credential_session_manager.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/refresh_coordinator.dart';

import 'auth_test_fakes.dart';

void main() {
  test(
    'concurrent 401s share one refresh and replay each request once',
    () async {
      var refreshCalls = 0;
      var oldTokenRequests = 0;
      var newTokenRequests = 0;
      final harness = _harness((request) async {
        if (request.uri.path == '/api/auth/refresh-token') {
          refreshCalls++;
          expect(request.data, {
            'token': 'access-one',
            'refreshToken': 'refresh-one',
          });
          await Future<void>.delayed(const Duration(milliseconds: 30));
          return jsonResponse(_replacement(), 200);
        }
        if (request.uri.path == '/api/auth/me' ||
            request.uri.path == '/api/me/outlets') {
          if (request.headers['Authorization'] == 'Bearer access-one') {
            oldTokenRequests++;
            return jsonResponse({'message': 'Expired'}, 401);
          }
          newTokenRequests++;
          return jsonResponse({'ok': true}, 200);
        }
        return jsonResponse({'message': 'Not found'}, 404);
      });
      await harness.seed(samplePair);

      await Future.wait([
        harness.authenticated.get<Object?>('api/auth/me'),
        harness.authenticated.get<Object?>('api/me/outlets'),
      ]);

      expect(refreshCalls, 1);
      expect(oldTokenRequests, 2);
      expect(newTokenRequests, 2);
      expect(harness.vault.pair?.accessToken, 'access-two');
      expect(harness.vault.pair?.refreshToken, 'refresh-two');
    },
  );

  test('403 never starts refresh', () async {
    var refreshCalls = 0;
    final harness = _harness((request) async {
      if (request.uri.path == '/api/auth/refresh-token') refreshCalls++;
      return jsonResponse({'message': 'Forbidden'}, 403);
    });
    await harness.seed(samplePair);

    await expectLater(
      harness.authenticated.get<Object?>('api/auth/me'),
      throwsA(isA<DioException>()),
    );
    expect(refreshCalls, 0);
    expect(harness.vault.pair?.refreshToken, samplePair.refreshToken);
  });

  test('429 is surfaced without refresh', () async {
    var refreshCalls = 0;
    final harness = _harness((request) async {
      if (request.uri.path == '/api/auth/refresh-token') refreshCalls++;
      return jsonResponse({'message': 'Too many requests'}, 429);
    });
    await harness.seed(samplePair);

    await expectLater(
      harness.authenticated.get<Object?>('api/auth/me'),
      throwsA(isA<DioException>()),
    );
    expect(refreshCalls, 0);
    expect(harness.vault.pair, samplePair);
  });

  test('an ambiguous refresh timeout clears credentials and does not retry old token', () async {
    var refreshCalls = 0;
    var requestCalls = 0;
    final harness = _harness((request) async {
      if (request.uri.path == '/api/auth/refresh-token') {
        refreshCalls++;
        throw DioException(
          requestOptions: request,
          type: DioExceptionType.connectionTimeout,
        );
      }
      requestCalls++;
      return jsonResponse({'message': 'Expired'}, 401);
    });
    await harness.seed(samplePair);

    await expectLater(
      harness.authenticated.get<Object?>('api/auth/me'),
      throwsA(
        isA<DioException>().having(
          (error) => (error.error as AuthFailure).kind,
          'kind',
          AuthFailureKind.sessionEnded,
        ),
      ),
    );
    expect(refreshCalls, 1);
    expect(requestCalls, 1);
    expect(harness.vault.pair, isNull);
  });

  test('definitive refresh rejection clears the previous pair', () async {
    var refreshCalls = 0;
    final harness = _harness((request) async {
      if (request.uri.path == '/api/auth/refresh-token') {
        refreshCalls++;
        return jsonResponse({'message': 'Unauthorized'}, 401);
      }
      return jsonResponse({'message': 'Expired'}, 401);
    });
    await harness.seed(samplePair);

    await expectLater(
      harness.authenticated.get<Object?>('api/auth/me'),
      throwsA(isA<DioException>()),
    );
    expect(refreshCalls, 1);
    expect(harness.vault.pair, isNull);
  });

  test('an API 401 after replay clears the renewed credentials without another refresh', () async {
    var refreshCalls = 0;
    var endpointCalls = 0;
    final harness = _harness((request) async {
      if (request.uri.path == '/api/auth/refresh-token') {
        refreshCalls++;
        return jsonResponse(_replacement(), 200);
      }
      endpointCalls++;
      return jsonResponse({'message': 'Unauthorized'}, 401);
    });
    await harness.seed(samplePair);

    await expectLater(
      harness.authenticated.get<Object?>('api/auth/me'),
      throwsA(isA<DioException>()),
    );

    expect(refreshCalls, 1);
    expect(endpointCalls, 2);
    expect(harness.vault.pair, isNull);
  });

  test(
    'a late refresh cannot overwrite credentials from a newer login',
    () async {
      final refreshResponse = Completer<ResponseBody>();
      final harness = _harness((request) async {
        if (request.uri.path == '/api/auth/refresh-token') {
          return refreshResponse.future;
        }
        return jsonResponse({}, 200);
      });
      final oldGeneration = await harness.seed(samplePair);
      final refresh = harness.refresh.refresh(oldGeneration, samplePair);
      await Future<void>.delayed(const Duration(milliseconds: 10));
      final newGeneration = harness.credentials.beginSession();
      await harness.credentials.replace(newGeneration, replacementPair);
      refreshResponse.complete(jsonResponse(_replacement(), 200));

      await expectLater(refresh, throwsA(isA<AuthFailure>()));
      expect(harness.vault.pair, replacementPair);
    },
  );
}

_AuthHarness _harness(
  Future<ResponseBody> Function(RequestOptions request) handle,
) {
  final vault = MemoryTokenVault();
  final credentials = CredentialSessionManager(vault);
  final raw = Dio(BaseOptions(baseUrl: 'https://erp.example.test/'));
  final authenticated = Dio(BaseOptions(baseUrl: 'https://erp.example.test/'));
  final adapter = CallbackHttpAdapter(handle);
  raw.httpClientAdapter = adapter;
  authenticated.httpClientAdapter = adapter;
  final refresh = RefreshCoordinator(
    unauthenticatedDio: raw,
    isConfigured: true,
    credentials: credentials,
    mapper: const AuthResponseMapper(),
  );
  authenticated.interceptors.add(
    AuthSessionInterceptor(
      client: authenticated,
      credentials: credentials,
      refreshCoordinator: refresh,
    ),
  );
  return _AuthHarness(vault, credentials, authenticated, refresh);
}

Map<String, dynamic> _replacement() => {
  'token': 'access-two',
  'refreshToken': 'refresh-two',
  'expiresAt': '2030-01-02T00:00:00Z',
  'user': {'id': 4},
};

final class _AuthHarness {
  const _AuthHarness(
    this.vault,
    this.credentials,
    this.authenticated,
    this.refresh,
  );

  final MemoryTokenVault vault;
  final CredentialSessionManager credentials;
  final Dio authenticated;
  final RefreshCoordinator refresh;

  Future<int> seed(TokenPair pair) async {
    final generation = credentials.beginSession();
    await credentials.replace(generation, pair);
    return generation;
  }
}
