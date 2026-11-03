enum AuthFailureKind {
  invalidCredentials,
  passwordSetupRequired,
  unauthorized,
  forbidden,
  rateLimited,
  offline,
  server,
  configuration,
  secureStorage,
  invalidServerResponse,
  unsupportedAccount,
  sessionEnded,
}

final class AuthFailure implements Exception {
  const AuthFailure(this.kind);

  final AuthFailureKind kind;
}
