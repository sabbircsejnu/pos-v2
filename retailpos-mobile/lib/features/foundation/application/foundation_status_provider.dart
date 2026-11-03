import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iranimart_business_mobile/features/foundation/domain/foundation_status.dart';
import 'package:iranimart_business_mobile/features/foundation/domain/foundation_status_repository.dart';

final foundationStatusRepositoryProvider = Provider<FoundationStatusRepository>((
  ref,
) {
  throw StateError(
    'FoundationStatusRepository must be supplied by the app composition root.',
  );
});

final foundationStatusProvider = Provider<FoundationStatus>((ref) {
  return ref.watch(foundationStatusRepositoryProvider).read();
});
