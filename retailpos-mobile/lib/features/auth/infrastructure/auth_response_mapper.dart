import '../domain/auth_failure.dart';
import '../domain/auth_models.dart';

final class AuthResponseMapper {
  const AuthResponseMapper();

  TokenPair tokenPair(Map<String, dynamic> response) {
    final accessToken = response['token'];
    final refreshToken = response['refreshToken'];
    final expiresAt = response['expiresAt'];
    if (accessToken is! String ||
        accessToken.isEmpty ||
        refreshToken is! String ||
        refreshToken.isEmpty ||
        expiresAt is! String) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    final parsedExpiry = DateTime.tryParse(expiresAt);
    if (parsedExpiry == null) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    return TokenPair(
      accessToken: accessToken,
      refreshToken: refreshToken,
      expiresAt: parsedExpiry.toUtc(),
    );
  }

  int loginUserId(Map<String, dynamic> response) {
    final user = _map(response['user']);
    final id = _positiveId(user['id']);
    if (id == null) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    return id;
  }

  SessionContext sessionContext({
    required Map<String, dynamic> profileJson,
    required Map<String, dynamic> outletsJson,
    required int? expectedUserId,
  }) {
    final profile = _profile(profileJson);
    profile.ensureMobileBusinessAccount();
    if (expectedUserId != null && profile.id != expectedUserId) {
      throw const AuthFailure(AuthFailureKind.unsupportedAccount);
    }

    final rawOutlets = outletsJson['outlets'];
    if (rawOutlets is! List) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    final outlets = <AuthorizedOutlet>[];
    final seenIds = <int>{};
    for (final rawOutlet in rawOutlets) {
      final outlet = _map(rawOutlet);
      final id = _positiveId(outlet['id']);
      final name = outlet['name'];
      final type = outlet['type'];
      if (id == null || name is! String || type != 'outlet') {
        throw const AuthFailure(AuthFailureKind.invalidServerResponse);
      }
      if (!seenIds.add(id)) {
        throw const AuthFailure(AuthFailureKind.invalidServerResponse);
      }
      outlets.add(AuthorizedOutlet(id: id, name: name));
    }

    final serverDefault = _positiveId(outletsJson['defaultOutletId']);
    final selectedOutletId = seenIds.contains(serverDefault)
        ? serverDefault
        : outlets.length == 1
        ? outlets.single.id
        : null;
    return SessionContext(
      profile: profile,
      outlets: List.unmodifiable(outlets),
      selectedOutletId: selectedOutletId,
    );
  }

  UserProfile _profile(Map<String, dynamic> json) {
    final id = _positiveId(json['id']);
    final name = json['name'];
    final email = json['email'];
    final businessId = _positiveId(json['businessId']);
    if (id == null ||
        name is! String ||
        email is! String ||
        json['isRoleSwitched'] is! bool ||
        json['isBusinessOwner'] is! bool ||
        json['mustResetPassword'] is! bool) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    final role = json['roleName'];
    if (role != null && role is! String) {
      throw const AuthFailure(AuthFailureKind.invalidServerResponse);
    }
    return UserProfile(
      id: id,
      businessId: businessId,
      name: name,
      email: email,
      roleName: role as String?,
      isRoleSwitched: json['isRoleSwitched'] as bool,
      isBusinessOwner: json['isBusinessOwner'] as bool,
      mustResetPassword: json['mustResetPassword'] as bool,
    );
  }

  static Map<String, dynamic> _map(Object? value) {
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return Map<String, dynamic>.from(value);
    throw const AuthFailure(AuthFailureKind.invalidServerResponse);
  }

  static int? _positiveId(Object? value) {
    final id = switch (value) {
      int integer => integer,
      num number when number.isFinite && number == number.roundToDouble() =>
        number.toInt(),
      String string => int.tryParse(string),
      _ => null,
    };
    return id != null && id > 0 ? id : null;
  }
}
