import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/features/notifications/domain/notification_presentation_models.dart';
import 'package:iranimart_business_mobile/features/notifications/presentation/notification_centre_page.dart';

void main() {
  testWidgets(
    'notification centre shows authorized context and no synthetic alerts',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: NotificationCentrePage(
              businessId: 8,
              outletName: 'North Outlet',
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Business 8'), findsOneWidget);
      expect(find.text('North Outlet'), findsOneWidget);
      expect(
        find.textContaining('determined by ERP permissions'),
        findsOneWidget,
      );
      expect(find.text('Inbox not connected'), findsOneWidget);
      expect(find.text('Mark all read · Not connected'), findsOneWidget);
      expect(find.text('Sale discount exceeded'), findsNothing);
      await tester.tap(find.text('Unread'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('Notification history unavailable'),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('Notification history unavailable'), findsOneWidget);
      expect(find.textContaining('Filter: Unread'), findsOneWidget);
    },
  );

  testWidgets(
    'filters, unread badge and read action update presentation only',
    (tester) async {
      final notifications = [
        _notification(
          id: 'fixture-unread',
          title: 'Synthetic unread approval',
          type: NotificationType.stockTakeSubmitted,
        ),
        _notification(
          id: 'fixture-read',
          title: 'Synthetic read stock alert',
          type: NotificationType.inventoryLowStock,
          isRead: true,
        ),
        _notification(
          id: 'fixture-unread-sales',
          title: 'Synthetic unread sale',
          type: NotificationType.salesDiscountThresholdExceeded,
        ),
      ];
      final harness = _NotificationPreviewHarness(notifications: notifications);
      await tester.pumpWidget(MaterialApp(home: harness));
      await tester.pumpAndSettle();

      expect(find.text('2'), findsOneWidget);
      expect(find.text('Synthetic unread approval'), findsOneWidget);
      expect(find.text('Synthetic read stock alert'), findsOneWidget);
      expect(find.text('Synthetic unread sale'), findsOneWidget);

      await tester.tap(find.text('Unread only'));
      await tester.pumpAndSettle();
      expect(find.text('Synthetic read stock alert'), findsNothing);
      expect(find.text('Synthetic unread approval'), findsOneWidget);
      expect(find.text('Synthetic unread sale'), findsOneWidget);

      await tester.tap(find.widgetWithText(TextButton, 'Inventory'));
      await tester.pumpAndSettle();
      expect(find.text('Synthetic read stock alert'), findsOneWidget);
      expect(find.text('Synthetic unread approval'), findsOneWidget);
      expect(find.text('Synthetic unread sale'), findsNothing);

      await tester.pumpAndSettle();
      await tester.tap(find.text('Mark as read · Preview only'));
      await tester.pumpAndSettle();
      expect(
        tester
            .state<_NotificationPreviewHarnessState>(
              find.byType(_NotificationPreviewHarness),
            )
            .markReadCalls,
        1,
      );
      expect(find.text('1'), findsOneWidget);
      expect(
        find.textContaining('read state is not synchronized'),
        findsNothing,
      );
    },
  );

  testWidgets('notification type filter selects matching history items', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NotificationHistoryPanel(
            state: NotificationInboxState.available,
            notifications: [
              _notification(
                id: 'fixture-type-match',
                title: 'Synthetic low-stock event',
                type: NotificationType.inventoryLowStock,
              ),
              _notification(
                id: 'fixture-type-other',
                title: 'Synthetic sale event',
                type: NotificationType.salesRefundCompleted,
              ),
            ],
            filter: const NotificationFilter.type(
              NotificationType.inventoryLowStock,
            ),
          ),
        ),
      ),
    );

    expect(find.text('Synthetic low-stock event'), findsOneWidget);
    expect(find.text('Synthetic sale event'), findsNothing);
  });

  testWidgets('all notification history states are explicit', (tester) async {
    const states = {
      NotificationInboxState.loading: 'Loading notifications',
      NotificationInboxState.empty: 'No notifications',
      NotificationInboxState.unavailable: 'Notification history unavailable',
      NotificationInboxState.error: 'Inbox could not be loaded',
      NotificationInboxState.stale: 'Inbox may be out of date',
      NotificationInboxState.unauthorized: 'Inbox access not authorized',
    };
    for (final entry in states.entries) {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: NotificationHistoryPanel(state: entry.key)),
        ),
      );
      expect(find.text(entry.value), findsOneWidget);
      expect(tester.takeException(), isNull);
    }
  });

  testWidgets(
    'notification list shows severity, type category and UTC event time',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NotificationHistoryPanel(
              state: NotificationInboxState.available,
              notifications: [
                _notification(
                  id: 'fixture-list',
                  title: 'Synthetic stock alert',
                  type: NotificationType.inventoryCriticalStock,
                  severity: NotificationSeverity.high,
                ),
              ],
            ),
          ),
        ),
      );

      expect(find.text('Synthetic stock alert'), findsOneWidget);
      expect(find.text('High'), findsOneWidget);
      expect(find.text('Inventory'), findsOneWidget);
      expect(find.text('2026-10-14 21:05 UTC'), findsOneWidget);
      expect(find.text('Unread'), findsOneWidget);
    },
  );

  testWidgets(
    'detail deep links require a typed key and reauthorization state',
    (tester) async {
      var opened = 0;
      final notification = _notification(
        id: 'fixture-detail',
        title: 'Synthetic approval',
        type: NotificationType.stockTakeApproved,
        routeKey: NotificationRouteKey.stockTakeHistory,
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NotificationDetailPanel(
              notification: notification,
              destinationState: NotificationDestinationState.authorized,
              onOpenDestination: () => opened++,
            ),
          ),
        ),
      );

      expect(find.text('Notification details'), findsOneWidget);
      expect(find.text('Stock Take Approved'), findsOneWidget);
      expect(
        find.text('Open Stock Take history · Reauthorize'),
        findsOneWidget,
      );
      expect(find.text('Destination available'), findsOneWidget);
      await tester.tap(find.text('Open Stock Take history · Reauthorize'));
      expect(opened, 1);

      for (final (state, label) in [
        (NotificationDestinationState.unavailable, 'Destination unavailable'),
        (NotificationDestinationState.expired, 'Destination has expired'),
        (
          NotificationDestinationState.inaccessible,
          'Destination is not accessible',
        ),
      ]) {
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: NotificationDetailPanel(
                notification: notification,
                destinationState: state,
                onOpenDestination: () => opened++,
              ),
            ),
          ),
        );
        expect(find.text(label), findsOneWidget);
        expect(find.textContaining('Open Stock Take history'), findsNothing);
        expect(tester.takeException(), isNull);
      }
      expect(opened, 1);
    },
  );

  testWidgets('unread read-action remains explicit and preview-only', (
    tester,
  ) async {
    var markedRead = false;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NotificationDetailPanel(
            notification: _notification(
              id: 'fixture-action',
              title: 'Synthetic unread notification',
              type: NotificationType.expenseApprovalRequired,
            ),
            onMarkRead: () => markedRead = true,
          ),
        ),
      ),
    );

    expect(find.text('Read state:'), findsOneWidget);
    expect(find.text('Unread'), findsNWidgets(2));
    await tester.tap(find.text('Mark as read · Preview only'));
    expect(markedRead, isTrue);
    expect(
      find.textContaining('not synchronized with the ERP'),
      findsOneWidget,
    );
  });

  testWidgets('notification centre fits compact viewport with large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      const MaterialApp(
        home: MediaQuery(
          data: MediaQueryData(
            size: Size(320, 640),
            textScaler: TextScaler.linear(2),
          ),
          child: Scaffold(
            body: NotificationCentrePage(
              businessId: 8,
              outletName: 'North Outlet',
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('Notification preferences'),
      240,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Notification preferences'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}

NotificationPresentation _notification({
  required String id,
  required String title,
  required NotificationType type,
  NotificationSeverity severity = NotificationSeverity.medium,
  bool isRead = false,
  NotificationRouteKey? routeKey,
}) => NotificationPresentation(
  id: id,
  type: type,
  severity: severity,
  title: title,
  summary: 'Synthetic fixture for presentation tests only.',
  eventTimeUtc: DateTime.utc(2026, 10, 14, 21, 5),
  isRead: isRead,
  routeKey: routeKey,
  resourceId: routeKey == null ? null : 'synthetic-resource',
);

final class _NotificationPreviewHarness extends StatefulWidget {
  const _NotificationPreviewHarness({required this.notifications});

  final List<NotificationPresentation> notifications;

  @override
  State<_NotificationPreviewHarness> createState() =>
      _NotificationPreviewHarnessState();
}

final class _NotificationPreviewHarnessState
    extends State<_NotificationPreviewHarness> {
  late List<NotificationPresentation> _notifications;
  NotificationFilter _filter = const NotificationFilter.all();
  int markReadCalls = 0;

  @override
  void initState() {
    super.initState();
    _notifications = widget.notifications;
  }

  @override
  Widget build(BuildContext context) {
    final unreadCount = _notifications
        .where((notification) => !notification.isRead)
        .length;
    return Scaffold(
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          UnreadNotificationBadge(count: unreadCount),
          Wrap(
            spacing: 8,
            children: [
              TextButton(
                onPressed: () =>
                    setState(() => _filter = const NotificationFilter.all()),
                child: const Text('All'),
              ),
              TextButton(
                onPressed: () =>
                    setState(() => _filter = const NotificationFilter.unread()),
                child: const Text('Unread only'),
              ),
              TextButton(
                onPressed: () => setState(
                  () => _filter = const NotificationFilter.category(
                    NotificationCategory.inventory,
                  ),
                ),
                child: const Text('Inventory'),
              ),
            ],
          ),
          NotificationHistoryPanel(
            state: NotificationInboxState.available,
            notifications: _notifications,
            filter: _filter,
            onMarkRead: _markRead,
          ),
        ],
      ),
    );
  }

  void _markRead(NotificationPresentation notification) {
    markReadCalls++;
    setState(() {
      _notifications = [
        for (final item in _notifications)
          if (item.id == notification.id) item.copyWith(isRead: true) else item,
      ];
    });
  }
}
