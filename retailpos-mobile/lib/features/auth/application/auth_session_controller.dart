import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../domain/auth_failure.dart';
import '../domain/auth_models.dart';
import '../domain/session_repository.dart';
import 'session_use_cases.dart';

enum AuthStatus { restoring, signedOut, signingIn, signingOut, authenticated }

final class AuthState {
  const AuthState({
    required this.status,
    this.context,
    this.failure,
    this.remoteLogoutUnconfirmed = false,
  });

  const AuthState.restoring() : this(status: AuthStatus.restoring);

  final AuthStatus status;
  final SessionContext? context;
  final AuthFailureKind? failure;
  final bool remoteLogoutUnconfirmed;
}

final sessionRepositoryProvider = Provider<SessionRepository>((ref) {
  throw StateError('SessionRepository must be configured by app composition.');
});

final authSessionControllerProvider =
    NotifierProvider<AuthSessionController, AuthState>(
      AuthSessionController.new,
    );

final class AuthSessionController extends Notifier<AuthState> {
  int _operation = 0;

  @override
  AuthState build() {
    scheduleMicrotask(restore);
    return const AuthState.restoring();
  }

  Future<void> restore() async {
    final operation = ++_operation;
    state = const AuthState.restoring();
    try {
      final context = await RestoreSession(
        ref.read(sessionRepositoryProvider),
      )();
      if (operation == _operation) {
        state = AuthState(
          status: context == null
              ? AuthStatus.signedOut
              : AuthStatus.authenticated,
          context: context,
        );
      }
    } on AuthFailure catch (failure) {
      if (operation == _operation) {
        state = AuthState(status: AuthStatus.signedOut, failure: failure.kind);
      }
    }
  }

  Future<void> signIn({required String email, required String password}) async {
    final operation = ++_operation;
    state = const AuthState(status: AuthStatus.signingIn);
    try {
      final context = await SignIn(ref.read(sessionRepositoryProvider))(
        email: email.trim(),
        password: password,
      );
      if (operation == _operation) {
        state = AuthState(status: AuthStatus.authenticated, context: context);
      }
    } on AuthFailure catch (failure) {
      if (operation == _operation) {
        state = AuthState(status: AuthStatus.signedOut, failure: failure.kind);
      }
    }
  }

  void selectOutlet(int outletId) {
    final current = state;
    final context = current.context;
    if (current.status != AuthStatus.authenticated || context == null) {
      return;
    }
    try {
      state = AuthState(
        status: AuthStatus.authenticated,
        context: context.selectOutlet(outletId),
      );
    } on AuthFailure catch (failure) {
      state = AuthState(
        status: AuthStatus.authenticated,
        context: context,
        failure: failure.kind,
      );
    }
  }

  Future<void> signOut() async {
    final operation = ++_operation;
    state = const AuthState(
      status: AuthStatus.signingOut,
      remoteLogoutUnconfirmed: true,
    );
    try {
      final result = await SignOut(ref.read(sessionRepositoryProvider))();
      if (operation == _operation) {
        state = AuthState(
          status: AuthStatus.signedOut,
          remoteLogoutUnconfirmed: !result.remoteRevocationConfirmed,
        );
      }
    } on AuthFailure catch (failure) {
      if (operation == _operation) {
        state = AuthState(
          status: AuthStatus.signedOut,
          failure: failure.kind,
          remoteLogoutUnconfirmed: true,
        );
      }
    }
  }
}
