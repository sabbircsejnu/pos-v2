enum NotificationCategory {
  sales('Sales'),
  inventory('Inventory'),
  purchase('Purchase'),
  accounting('Accounting'),
  forecasting('Forecasting'),
  system('System');

  const NotificationCategory(this.label);

  final String label;
}

enum NotificationSeverity {
  info('Information'),
  low('Low'),
  medium('Medium'),
  high('High'),
  critical('Critical');

  const NotificationSeverity(this.label);

  final String label;
}

enum NotificationType {
  salesDiscountThresholdExceeded(
    'sales.discount.threshold_exceeded',
    NotificationCategory.sales,
  ),
  salesDiscountAnomalous(
    'sales.discount.anomalous',
    NotificationCategory.sales,
  ),
  salesRefundCompleted('sales.refund.completed', NotificationCategory.sales),
  salesLargeSaleCompleted(
    'sales.large_sale.completed',
    NotificationCategory.sales,
  ),
  salesTargetAchieved('sales.target.achieved', NotificationCategory.sales),
  salesTargetMissed('sales.target.missed', NotificationCategory.sales),
  salesPerformanceDrop('sales.performance.drop', NotificationCategory.sales),
  inventoryLowStock('inventory.low_stock', NotificationCategory.inventory),
  inventoryCriticalStock(
    'inventory.critical_stock',
    NotificationCategory.inventory,
  ),
  inventoryOutOfStock('inventory.out_of_stock', NotificationCategory.inventory),
  stockTakeSubmitted('stocktake.submitted', NotificationCategory.inventory),
  stockTakeRejected('stocktake.rejected', NotificationCategory.inventory),
  stockTakeApproved('stocktake.approved', NotificationCategory.inventory),
  stockTakeVarianceDetected(
    'stocktake.variance_detected',
    NotificationCategory.inventory,
  ),
  inventoryAdjustmentPosted(
    'inventory.adjustment.posted',
    NotificationCategory.inventory,
  ),
  stockTransferApprovalRequired(
    'stocktransfer.approval_required',
    NotificationCategory.inventory,
  ),
  stockTransferReceived(
    'stocktransfer.received',
    NotificationCategory.inventory,
  ),
  purchaseOrderApprovalRequired(
    'purchaseorder.approval_required',
    NotificationCategory.purchase,
  ),
  goodsReceiptCompleted(
    'goodsreceipt.completed',
    NotificationCategory.purchase,
  ),
  expenseLargeSubmitted(
    'expense.large_submitted',
    NotificationCategory.accounting,
  ),
  expenseApprovalRequired(
    'expense.approval_required',
    NotificationCategory.accounting,
  ),
  supplierPaymentCompleted(
    'supplierpayment.completed',
    NotificationCategory.accounting,
  ),
  cashSessionVarianceDetected(
    'cashsession.variance_detected',
    NotificationCategory.accounting,
  ),
  forecastReady('forecast.ready', NotificationCategory.forecasting),
  forecastStockoutPredicted(
    'forecast.stockout_predicted',
    NotificationCategory.forecasting,
  ),
  forecastProductionRecommendationReady(
    'forecast.production_recommendation_ready',
    NotificationCategory.forecasting,
  ),
  systemBackgroundJobFailed(
    'system.background_job_failed',
    NotificationCategory.system,
  ),
  systemNotificationDeliveryFailed(
    'system.notification_delivery_failed',
    NotificationCategory.system,
  );

  const NotificationType(this.code, this.category);

  final String code;
  final NotificationCategory category;

  String get label {
    final words = name.replaceAllMapped(
      RegExp(r'([a-z])([A-Z])'),
      (match) => '${match[1]} ${match[2]}',
    );
    return '${words[0].toUpperCase()}${words.substring(1)}';
  }
}

enum NotificationFilterKind { all, unread, category, type }

final class NotificationFilter {
  const NotificationFilter.all()
    : kind = NotificationFilterKind.all,
      category = null,
      type = null;

  const NotificationFilter.unread()
    : kind = NotificationFilterKind.unread,
      category = null,
      type = null;

  const NotificationFilter.category(this.category)
    : kind = NotificationFilterKind.category,
      type = null;

  const NotificationFilter.type(this.type)
    : kind = NotificationFilterKind.type,
      category = null;

  final NotificationFilterKind kind;
  final NotificationCategory? category;
  final NotificationType? type;

  String get label => switch (kind) {
    NotificationFilterKind.all => 'All',
    NotificationFilterKind.unread => 'Unread',
    NotificationFilterKind.category => category!.label,
    NotificationFilterKind.type => type!.label,
  };

  bool includes(NotificationPresentation notification) => switch (kind) {
    NotificationFilterKind.all => true,
    NotificationFilterKind.unread => !notification.isRead,
    NotificationFilterKind.category => notification.type.category == category,
    NotificationFilterKind.type => notification.type == type,
  };
}

enum NotificationRouteKey {
  stockTakeHistory('Stock Take history'),
  stockTakeReview('Stock Take review'),
  saleDetails('Sale details'),
  inventoryDetails('Inventory details'),
  approvalQueue('Approval queue'),
  purchaseOrderDetails('Purchase order details'),
  expenseDetails('Expense details'),
  forecastDetails('Forecast details');

  const NotificationRouteKey(this.label);

  final String label;
}

enum NotificationDestinationState {
  authorized('Destination available'),
  unavailable('Destination unavailable'),
  expired('Destination has expired'),
  inaccessible('Destination is not accessible');

  const NotificationDestinationState(this.label);

  final String label;
}

final class NotificationPresentation {
  const NotificationPresentation({
    required this.id,
    required this.type,
    required this.severity,
    required this.title,
    required this.summary,
    required this.eventTimeUtc,
    required this.isRead,
    this.routeKey,
    this.resourceId,
  });

  final String id;
  final NotificationType type;
  final NotificationSeverity severity;
  final String title;
  final String summary;
  final DateTime eventTimeUtc;
  final bool isRead;
  final NotificationRouteKey? routeKey;
  final String? resourceId;

  NotificationPresentation copyWith({bool? isRead}) => NotificationPresentation(
    id: id,
    type: type,
    severity: severity,
    title: title,
    summary: summary,
    eventTimeUtc: eventTimeUtc,
    isRead: isRead ?? this.isRead,
    routeKey: routeKey,
    resourceId: resourceId,
  );
}
