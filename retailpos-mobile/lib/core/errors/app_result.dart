sealed class AppFailure implements Exception {
  const AppFailure(this.code);

  final String code;
}

final class ConfigurationFailure extends AppFailure {
  const ConfigurationFailure(super.code);
}

final class TransportFailure extends AppFailure {
  const TransportFailure(super.code);
}

sealed class AppResult<T> {
  const AppResult();
}

final class AppSuccess<T> extends AppResult<T> {
  const AppSuccess(this.value);

  final T value;
}

final class AppFailureResult<T> extends AppResult<T> {
  const AppFailureResult(this.failure);

  final AppFailure failure;
}
