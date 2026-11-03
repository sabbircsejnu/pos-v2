import 'auth_models.dart';

abstract interface class SessionRepository {
  Future<SessionContext?> restoreSession();

  Future<SessionContext> signIn({
    required String email,
    required String password,
  });

  Future<LogoutResult> signOut();
}
