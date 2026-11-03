import '../domain/auth_failure.dart';
import '../domain/auth_models.dart';
import '../domain/session_repository.dart';
import 'auth_api_remote_data_source.dart';
import 'auth_response_mapper.dart';
import 'credential_session_manager.dart';

final class SessionRepositoryImpl implements SessionRepository {
  SessionRepositoryImpl({
    required this._remote,
    required this._credentials,
    required this._mapper,
  });

  final AuthApiRemoteDataSource _remote;
  final CredentialSessionManager _credentials;
  final AuthResponseMapper _mapper;

  @override
  Future<SessionContext?> restoreSession() async {
    final generation = _credentials.beginSession();
    final pair = await _credentials.restore(generation);
    if (pair == null) return null;
    try {
      return await _fetchContext(generation);
    } on AuthFailure catch (failure) {
      if (failure.kind != AuthFailureKind.offline &&
          failure.kind != AuthFailureKind.rateLimited) {
        await _credentials.clear(generation);
      }
      rethrow;
    }
  }

  @override
  Future<SessionContext> signIn({
    required String email,
    required String password,
  }) async {
    final generation = _credentials.beginSession();
    await _credentials.clear(generation);
    try {
      final response = await _remote.login(email: email, password: password);
      final pair = _mapper.tokenPair(response);
      final loginUserId = _mapper.loginUserId(response);
      if (!await _credentials.replace(generation, pair)) {
        throw const AuthFailure(AuthFailureKind.sessionEnded);
      }
      return await _fetchContext(generation, expectedUserId: loginUserId);
    } on AuthFailure catch (failure) {
      if (failure.kind != AuthFailureKind.offline &&
          failure.kind != AuthFailureKind.rateLimited) {
        await _credentials.clear(generation);
      }
      rethrow;
    }
  }

  @override
  Future<LogoutResult> signOut() async {
    TokenPair? pair;
    var credentialsReadable = true;
    final currentGeneration = _credentials.generation;
    try {
      pair = await _credentials.read(currentGeneration);
    } on AuthFailure {
      credentialsReadable = false;
    }

    final generation = _credentials.beginSession();
    var remoteRevocationConfirmed = false;
    if (credentialsReadable && pair != null) {
      try {
        await _remote.logout(pair);
        remoteRevocationConfirmed = true;
      } on AuthFailure {
        remoteRevocationConfirmed = false;
      }
    }
    await _credentials.clear(generation);
    return LogoutResult(remoteRevocationConfirmed: remoteRevocationConfirmed);
  }

  Future<SessionContext> _fetchContext(
    int generation, {
    int? expectedUserId,
  }) async {
    if (!_credentials.isCurrent(generation)) {
      throw const AuthFailure(AuthFailureKind.sessionEnded);
    }
    final profile = await _remote.currentUser();
    final outlets = await _remote.authorizedOutlets();
    if (!_credentials.isCurrent(generation)) {
      throw const AuthFailure(AuthFailureKind.sessionEnded);
    }
    return _mapper.sessionContext(
      profileJson: profile,
      outletsJson: outlets,
      expectedUserId: expectedUserId,
    );
  }
}
