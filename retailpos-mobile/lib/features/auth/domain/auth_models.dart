import 'auth_failure.dart';

final class TokenPair {
  const TokenPair({
    required this.accessToken,
    required this.refreshToken,
    required this.expiresAt,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime expiresAt;

  bool get isUsable =>
      accessToken.isNotEmpty && refreshToken.isNotEmpty && expiresAt.isUtc;

  bool hasSameSecrets(TokenPair other) =>
      accessToken == other.accessToken && refreshToken == other.refreshToken;
}

final class UserProfile {
  const UserProfile({
    required this.id,
    required this.businessId,
    required this.name,
    required this.email,
    required this.roleName,
    required this.isRoleSwitched,
    required this.isBusinessOwner,
    required this.mustResetPassword,
  });

  final int id;
  final int? businessId;
  final String name;
  final String email;
  final String? roleName;
  final bool isRoleSwitched;
  final bool isBusinessOwner;
  final bool mustResetPassword;

  void ensureMobileBusinessAccount() {
    if (id <= 0 || businessId == null || businessId! <= 0) {
      throw const AuthFailure(AuthFailureKind.unsupportedAccount);
    }
    final normalizedRole = roleName
        ?.replaceAll(RegExp(r'[\s_-]'), '')
        .toLowerCase();
    if (isRoleSwitched || normalizedRole == 'superadmin') {
      throw const AuthFailure(AuthFailureKind.unsupportedAccount);
    }
    if (mustResetPassword) {
      throw const AuthFailure(AuthFailureKind.passwordSetupRequired);
    }
  }
}

final class AuthorizedOutlet {
  const AuthorizedOutlet({required this.id, required this.name});

  final int id;
  final String name;
}

final class SessionContext {
  const SessionContext({
    required this.profile,
    required this.outlets,
    required this.selectedOutletId,
  });

  final UserProfile profile;
  final List<AuthorizedOutlet> outlets;
  final int? selectedOutletId;

  SessionContext selectOutlet(int outletId) {
    if (!outlets.any((outlet) => outlet.id == outletId)) {
      throw const AuthFailure(AuthFailureKind.forbidden);
    }
    return SessionContext(
      profile: profile,
      outlets: outlets,
      selectedOutletId: outletId,
    );
  }
}

final class LogoutResult {
  const LogoutResult({required this.remoteRevocationConfirmed});

  final bool remoteRevocationConfirmed;
}
