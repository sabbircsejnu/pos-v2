import '../domain/auth_models.dart';
import '../domain/session_repository.dart';

final class RestoreSession {
  const RestoreSession(this._repository);

  final SessionRepository _repository;

  Future<SessionContext?> call() => _repository.restoreSession();
}

final class SignIn {
  const SignIn(this._repository);

  final SessionRepository _repository;

  Future<SessionContext> call({
    required String email,
    required String password,
  }) => _repository.signIn(email: email, password: password);
}

final class SignOut {
  const SignOut(this._repository);

  final SessionRepository _repository;

  Future<LogoutResult> call() => _repository.signOut();
}
