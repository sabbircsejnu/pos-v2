import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_failure.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';
import 'package:iranimart_business_mobile/features/auth/domain/session_repository.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/secure_token_vault.dart';

final class MemorySecureStorage implements SecureStorageAdapter {
  String? value;
  bool failRead = false;
  bool failWrite = false;
  bool writePartialThenFail = false;

  @override
  Future<String?> read({required String key}) async {
    if (failRead) throw StateError('storage failure');
    return value;
  }

  @override
  Future<void> write({required String key, required String value}) async {
    if (writePartialThenFail) {
      this.value = '{"version":1,"accessToken":"interrupted';
      throw StateError('interrupted storage write');
    }
    if (failWrite) throw StateError('storage failure');
    this.value = value;
  }

  @override
  Future<void> delete({required String key}) async {
    value = null;
  }
}

final class MemoryTokenVault implements TokenVault {
  TokenPair? pair;
  bool failRead = false;
  bool failReplace = false;
  bool failClear = false;

  @override
  Future<TokenPair?> read() async {
    if (failRead) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
    return pair;
  }

  @override
  Future<void> replace(TokenPair pair) async {
    if (failReplace) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
    this.pair = pair;
  }

  @override
  Future<void> clear() async {
    if (failClear) {
      throw const AuthFailure(AuthFailureKind.secureStorage);
    }
    pair = null;
  }
}

final class CallbackHttpAdapter implements HttpClientAdapter {
  CallbackHttpAdapter(this.onRequest);

  final Future<ResponseBody> Function(RequestOptions options) onRequest;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) => onRequest(options);

  @override
  void close({bool force = false}) {}
}

ResponseBody jsonResponse(Object? body, int statusCode) =>
    ResponseBody.fromString(
      jsonEncode(body),
      statusCode,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );

final class FakeSessionRepository implements SessionRepository {
  FakeSessionRepository({this.context, this.restoreContext});

  SessionContext? context;
  SessionContext? restoreContext;
  AuthFailure? signInFailure;
  LogoutResult logoutResult = const LogoutResult(
    remoteRevocationConfirmed: true,
  );
  int restoreCalls = 0;
  int signInCalls = 0;
  int signOutCalls = 0;

  @override
  Future<SessionContext?> restoreSession() async {
    restoreCalls++;
    return restoreContext;
  }

  @override
  Future<SessionContext> signIn({
    required String email,
    required String password,
  }) async {
    signInCalls++;
    if (signInFailure case final failure?) throw failure;
    return context ?? sampleSessionContext;
  }

  @override
  Future<LogoutResult> signOut() async {
    signOutCalls++;
    context = null;
    return logoutResult;
  }
}

final samplePair = TokenPair(
  accessToken: 'access-one',
  refreshToken: 'refresh-one',
  expiresAt: DateTime.utc(2030),
);

final replacementPair = TokenPair(
  accessToken: 'access-two',
  refreshToken: 'refresh-two',
  expiresAt: DateTime.utc(2030, 1, 2),
);

final sampleSessionContext = SessionContext(
  profile: const UserProfile(
    id: 4,
    businessId: 8,
    name: 'A User',
    email: 'user@example.test',
    roleName: 'Business Owner',
    isRoleSwitched: false,
    isBusinessOwner: true,
    mustResetPassword: false,
  ),
  outlets: const [
    AuthorizedOutlet(id: 21, name: 'North Outlet'),
    AuthorizedOutlet(id: 22, name: 'South Outlet'),
  ],
  selectedOutletId: 21,
);
