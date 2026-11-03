enum StockTakeSection {
  overview('Overview'),
  create('Create stock take'),
  count('Scan and count'),
  synchronization('Offline and sync'),
  review('Count review'),
  reconciliation('Submission and reconciliation'),
  history('Session history');

  const StockTakeSection(this.label);

  final String label;

  static StockTakeSection fromQuery(String? value) => values.firstWhere(
    (section) => section.name == value,
    orElse: () => StockTakeSection.overview,
  );
}

enum StockTakeScope {
  fullOutlet('Full outlet'),
  category('Category'),
  selectedVariants('Selected variants');

  const StockTakeScope(this.label);

  final String label;
}

enum StockTakeCollectionState {
  loading,
  empty,
  unavailable,
  stale,
  error,
  unauthorized,
}

enum StockTakeSessionStatus {
  draft('Draft'),
  inProgress('In progress'),
  submitted('Submitted'),
  underReview('Under review'),
  approved('Approved'),
  rejected('Rejected'),
  adjusted('Adjusted'),
  cancelled('Cancelled');

  const StockTakeSessionStatus(this.label);

  final String label;
}

enum StockTakeSyncStatus {
  unavailable('Unavailable'),
  offlineReady('Offline-ready'),
  staleCatalogue('Catalogue stale'),
  pending('Pending changes'),
  syncing('Synchronizing'),
  failed('Sync failed'),
  conflicted('Conflict requires review'),
  serverAccepted('Server accepted'),
  sessionExpired('Session expired');

  const StockTakeSyncStatus(this.label);

  final String label;
}

enum StockTakeBarcodeException {
  unknown('Unknown barcode'),
  ambiguous('Ambiguous barcode'),
  duplicate('Duplicate barcode'),
  retired('Retired or reassigned barcode'),
  outOfScope('Barcode outside the session scope');

  const StockTakeBarcodeException(this.label);

  final String label;
}

enum StockTakeSubmissionStatus {
  unavailable('Unavailable'),
  localPending('Local changes pending'),
  serverAccepted('Accepted by server'),
  underReview('Under review'),
  approved('Approved'),
  rejected('Rejected'),
  posted('Inventory adjustment posted');

  const StockTakeSubmissionStatus(this.label);

  final String label;
}

enum StockTakeHistoryFilter {
  all('All sessions'),
  active('Active'),
  pending('Pending review'),
  completed('Completed');

  const StockTakeHistoryFilter(this.label);

  final String label;
}

final class StockTakeVariantIdentity {
  const StockTakeVariantIdentity({
    required this.productId,
    required this.productVariantId,
    required this.productName,
    required this.mainProductCode,
    required this.barcode,
    this.attributes = const [],
  });

  final int productId;
  final int productVariantId;
  final String productName;
  final String mainProductCode;
  final String barcode;
  final List<StockTakeAttributeValue> attributes;
}

final class StockTakeAttributeValue {
  const StockTakeAttributeValue({required this.name, required this.value});

  final String name;
  final String value;
}

final class StockTakeTimelineEntry {
  const StockTakeTimelineEntry({
    required this.action,
    required this.actor,
    required this.occurredAt,
  });

  final String action;
  final String actor;
  final DateTime occurredAt;
}

final class StockTakeSessionSummary {
  const StockTakeSessionSummary({
    required this.reference,
    required this.outletName,
    required this.scopeLabel,
    required this.status,
    required this.updatedAt,
  });

  final String reference;
  final String outletName;
  final String scopeLabel;
  final StockTakeSessionStatus status;
  final DateTime updatedAt;
}

final class StockTakeCountReviewLine {
  const StockTakeCountReviewLine({
    required this.identity,
    required this.physicalQuantity,
  });

  final StockTakeVariantIdentity identity;
  final int physicalQuantity;
}

final class StockTakeReconciliationValues {
  const StockTakeReconciliationValues({
    this.systemQuantity,
    this.countedQuantity,
    this.variance,
  });

  final int? systemQuantity;
  final int? countedQuantity;
  final int? variance;
}
