import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/stock_take/domain/stock_take_presentation_models.dart';
import 'package:iranimart_business_mobile/features/stock_take/presentation/stock_take_page.dart';

void main() {

  test('a measured count is refused out loud, never truncated', () {
    expect(stockTakeWholeCountError('12'), isNull);
    expect(stockTakeWholeCountError(''), isNull);
    expect(
      stockTakeWholeCountError('2.5'),
      'Whole pieces only. Count measured items on the web.',
    );
    expect(stockTakeWholeCountError('-1'), 'A count cannot be negative.');
    expect(stockTakeWholeCountError('abc'), 'Enter a whole number.');
  });

  testWidgets('typing a decimal count shows the error and reports nothing', (
    tester,
  ) async {
    final reported = <int>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: StockTakeCountControls(
            quantity: 3,
            enabled: true,
            onChanged: reported.add,
          ),
        ),
      ),
    );

    await tester.enterText(
      find.byKey(const ValueKey('stock-take-quantity')),
      '2.5',
    );
    await tester.pump();

    expect(
      find.text('Whole pieces only. Count measured items on the web.'),
      findsOneWidget,
    );
    expect(reported, isEmpty);
  });
  testWidgets('Stock Take overview keeps session lists unavailable', (
    tester,
  ) async {
    await tester.pumpWidget(_page());

    expect(find.text('Stock Take'), findsOneWidget);
    expect(find.text('Business 8'), findsOneWidget);
    expect(
      find.text('Authorized outlet context: North Outlet'),
      findsOneWidget,
    );
    expect(find.text('Active session unavailable'), findsOneWidget);
    expect(find.text('Pending sessions unavailable'), findsOneWidget);
    expect(find.text('History unavailable'), findsOneWidget);
    expect(
      find.textContaining('No active or available session is shown'),
      findsOneWidget,
    );
  });

  testWidgets(
    'session collections model empty, stale, error and denied states',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SingleChildScrollView(
              child: Column(
                children: const [
                  StockTakeCollectionStatePanel(
                    state: StockTakeCollectionState.empty,
                    title: 'No sessions',
                    message: 'No sessions exist in this authorized scope.',
                  ),
                  StockTakeCollectionStatePanel(
                    state: StockTakeCollectionState.stale,
                    title: 'Session data stale',
                    message: 'Refresh before continuing.',
                  ),
                  StockTakeCollectionStatePanel(
                    state: StockTakeCollectionState.error,
                    title: 'Session list error',
                    message: 'Try again later.',
                  ),
                  StockTakeCollectionStatePanel(
                    state: StockTakeCollectionState.unauthorized,
                    title: 'Session list denied',
                    message: 'Access was not granted.',
                  ),
                ],
              ),
            ),
          ),
        ),
      );

      expect(find.text('No sessions'), findsOneWidget);
      expect(find.text('Session data stale'), findsOneWidget);
      expect(find.text('Session list error'), findsOneWidget);
      expect(find.text('Session list denied'), findsOneWidget);
    },
  );

  testWidgets(
    'create scope controls are local and session creation is disabled',
    (tester) async {
      await tester.pumpWidget(_page(section: StockTakeSection.create));

      await tester.tap(find.byKey(const ValueKey('stock-take-scope-selector')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Category').last);
      await tester.pumpAndSettle();

      expect(find.text('Category selection unavailable'), findsOneWidget);
      final start = tester.widget<FilledButton>(
        find.widgetWithText(FilledButton, 'Start session unavailable'),
      );
      expect(start.onPressed, isNull);
      expect(
        find.textContaining('Choosing a display scope does not create'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'raw barcode keeps leading zeros and count actions stay disabled',
    (tester) async {
      await tester.pumpWidget(_page(section: StockTakeSection.count));

      final barcodeField = find.byKey(const ValueKey('stock-take-raw-barcode'));
      await tester.enterText(barcodeField, '000123450067');
      expect(
        tester
            .widget<EditableText>(
              find.descendant(
                of: barcodeField,
                matching: find.byType(EditableText),
              ),
            )
            .controller
            .text,
        '000123450067',
      );

      for (final tooltip in [
        'Increase physical count',
        'Decrease physical count',
      ]) {
        final control = tester
            .widgetList<IconButton>(find.byType(IconButton))
            .singleWhere((button) => button.tooltip == tooltip);
        expect(control.onPressed, isNull);
      }
      expect(
        tester
            .widget<TextFormField>(
              find.byKey(const ValueKey('stock-take-quantity')),
            )
            .enabled,
        isFalse,
      );
      expect(
        tester
            .widget<OutlinedButton>(
              find.widgetWithText(OutlinedButton, 'Lookup unavailable'),
            )
            .onPressed,
        isNull,
      );
    },
  );

  testWidgets('variant identity displays 64-bit IDs and lossless barcode', (
    tester,
  ) async {
    const identity = StockTakeVariantIdentity(
      productId: 9000000000000000001,
      productVariantId: 9000000000000000002,
      productName: 'Synthetic test variant',
      mainProductCode: 'TEST-FAMILY',
      barcode: '00001234567890',
      attributes: [
        StockTakeAttributeValue(name: 'Colour', value: 'Test Blue'),
        StockTakeAttributeValue(name: 'Size', value: 'XL'),
      ],
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ListView(
            children: const [StockTakeVariantIdentityPanel(identity: identity)],
          ),
        ),
      ),
    );

    expect(find.text('ProductId: 9000000000000000001'), findsOneWidget);
    expect(find.text('ProductVariantId: 9000000000000000002'), findsOneWidget);
    expect(find.text('Main Product Code: TEST-FAMILY'), findsOneWidget);
    expect(find.text('Variant barcode: 00001234567890'), findsOneWidget);
    expect(find.text('Colour: Test Blue'), findsOneWidget);
    expect(find.text('Size: XL'), findsOneWidget);
  });

  testWidgets(
    'session and review cards show typed status and proposed quantity',
    (tester) async {
      const identity = StockTakeVariantIdentity(
        productId: 42,
        productVariantId: 4201,
        productName: 'Fixture product',
        mainProductCode: 'FIXTURE',
        barcode: '000004201',
        attributes: [StockTakeAttributeValue(name: 'Size', value: 'L')],
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SingleChildScrollView(
              child: Column(
                children: [
                  StockTakeSessionSummaryCard(
                    session: StockTakeSessionSummary(
                      reference: 'TEST-SESSION',
                      outletName: 'Fixture outlet',
                      scopeLabel: 'Full outlet',
                      status: StockTakeSessionStatus.inProgress,
                      updatedAt: DateTime.utc(2026, 10, 15, 6),
                    ),
                  ),
                  const StockTakeCountReviewItemCard(
                    line: StockTakeCountReviewLine(
                      identity: identity,
                      physicalQuantity: 3,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );

      expect(find.text('TEST-SESSION'), findsOneWidget);
      expect(find.text('In progress'), findsOneWidget);
      expect(find.textContaining('Fixture outlet'), findsOneWidget);
      expect(find.text('Proposed physical quantity: 3'), findsOneWidget);
      expect(find.text('Variant barcode: 000004201'), findsOneWidget);
    },
  );

  testWidgets('unknown and ambiguous barcode states never resolve by guess', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: Column(
              children: [
                StockTakeBarcodeExceptionPanel(
                  issue: StockTakeBarcodeException.unknown,
                  rawBarcode: '0000000081',
                ),
                StockTakeBarcodeExceptionPanel(
                  issue: StockTakeBarcodeException.ambiguous,
                  rawBarcode: '0000000082',
                ),
              ],
            ),
          ),
        ),
      ),
    );

    expect(find.textContaining('Raw barcode "0000000081"'), findsOneWidget);
    expect(find.textContaining('Raw barcode "0000000082"'), findsOneWidget);
    expect(
      find.textContaining('It was not mapped to a variant.'),
      findsNWidgets(2),
    );
  });

  testWidgets(
    'sync and reconciliation states keep server acceptance distinct',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SingleChildScrollView(
              child: Column(
                children: const [
                  StockTakeSyncStatusPanel(
                    status: StockTakeSyncStatus.pending,
                    previewOnly: true,
                  ),
                  StockTakeSyncStatusPanel(
                    status: StockTakeSyncStatus.conflicted,
                    previewOnly: true,
                  ),
                  StockTakeSyncStatusPanel(
                    status: StockTakeSyncStatus.sessionExpired,
                    previewOnly: true,
                  ),
                  StockTakeSubmissionStatusPanel(
                    status: StockTakeSubmissionStatus.serverAccepted,
                    previewOnly: true,
                  ),
                  StockTakeSubmissionStatusPanel(
                    status: StockTakeSubmissionStatus.posted,
                    previewOnly: true,
                  ),
                  StockTakeReconciliationPanel(
                    status: StockTakeSubmissionStatus.serverAccepted,
                    values: StockTakeReconciliationValues(
                      systemQuantity: 20,
                      countedQuantity: 18,
                      variance: -2,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );

      expect(
        find.textContaining('Pending changes · state definition'),
        findsOneWidget,
      );
      expect(
        find.textContaining('Conflict requires review · state definition'),
        findsOneWidget,
      );
      expect(
        find.textContaining('Session expired · state definition'),
        findsOneWidget,
      );
      expect(
        find.textContaining('Accepted by server · state definition'),
        findsOneWidget,
      );
      expect(
        find.textContaining('Inventory adjustment posted · state definition'),
        findsOneWidget,
      );
      expect(find.textContaining('System quantity: 20'), findsOneWidget);
      expect(find.textContaining('Counted quantity: 18'), findsOneWidget);
      expect(find.textContaining('Variance: -2'), findsOneWidget);
      expect(
        find.textContaining('this does not mean it is approved or posted.'),
        findsNWidgets(2),
      );
    },
  );

  testWidgets('session history timeline shows actor, action and UTC time', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: StockTakeSessionTimeline(
            entries: [
              StockTakeTimelineEntry(
                action: 'Synthetic test event',
                actor: 'Synthetic test actor',
                occurredAt: DateTime.utc(2026, 10, 15, 6, 0),
              ),
            ],
          ),
        ),
      ),
    );

    expect(find.text('Synthetic test event'), findsOneWidget);
    expect(
      find.text('Synthetic test actor · 2026-10-15T06:00:00.000Z'),
      findsOneWidget,
    );
  });

  testWidgets('page remains usable on narrow screen with large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: const MediaQueryData(
            size: Size(320, 640),
            textScaler: TextScaler.linear(2),
          ),
          child: _page(section: StockTakeSection.count),
        ),
      ),
    );

    expect(find.text('Manual barcode or product search'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('unsupported role receives an unauthorized state', (
    tester,
  ) async {
    await tester.pumpWidget(_page(role: MobileRole.unsupported));

    expect(find.text('Stock Take unavailable'), findsOneWidget);
    expect(
      find.textContaining('No stock session data was requested'),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('stock-take-section-selector')),
      findsNothing,
    );
  });
}

Widget _page({
  StockTakeSection section = StockTakeSection.overview,
  MobileRole role = MobileRole.businessOwner,
}) => MaterialApp(
  home: Scaffold(
    body: StockTakePage(
      role: role,
      businessId: 8,
      outletName: 'North Outlet',
      hasInvalidSelectedOutlet: false,
      selectedSection: section,
      onSectionSelected: (_) {},
    ),
  ),
);
