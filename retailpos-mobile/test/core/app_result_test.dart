import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/errors/app_result.dart';

void main() {
  test('AppResult carries either typed success or failure', () {
    const AppResult<int> success = AppSuccess(42);
    const AppResult<int> failure = AppFailureResult(
      TransportFailure('network_unavailable'),
    );

    expect(success, isA<AppSuccess<int>>());
    expect((success as AppSuccess<int>).value, 42);
    expect(failure, isA<AppFailureResult<int>>());
    expect(
      (failure as AppFailureResult<int>).failure.code,
      'network_unavailable',
    );
  });
}
