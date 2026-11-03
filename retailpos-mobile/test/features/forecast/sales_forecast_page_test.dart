import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/forecast/domain/forecast_presentation_models.dart';
import 'package:iranimart_business_mobile/features/forecast/presentation/sales_forecast_page.dart';

void main() {
  testWidgets('owner sees disconnected forecast and local month horizons', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SalesForecastPage(
            role: MobileRole.businessOwner,
            businessId: 8,
            outletName: 'North Outlet',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Business 8'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
    expect(find.text('Forecast unavailable'), findsOneWidget);
    expect(find.text('7 days'), findsNothing);
    expect(find.text('1 month'), findsOneWidget);
    expect(find.text('2 months'), findsOneWidget);
    expect(find.text('3 months'), findsOneWidget);

    await tester.tap(find.text('2 months'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.byKey(const ValueKey('selected-forecast-horizon')),
      240,
      scrollable: _verticalScrollable,
    );
    expect(
      find.text(
        'Selected: 2 months. Presentation only; no forecast calculation or request.',
      ),
      findsOneWidget,
    );
  });

  testWidgets('unsupported forecast role receives an unauthorized state', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SalesForecastPage(
            role: MobileRole.manager,
            businessId: 8,
            outletName: 'North Outlet',
          ),
        ),
      ),
    );

    expect(find.text('Forecast unavailable'), findsOneWidget);
    expect(find.textContaining('not currently granted access'), findsOneWidget);
    expect(find.text('1 month'), findsNothing);
  });

  testWidgets('demand dimension filters do not display fabricated rankings', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SalesForecastPage(
            role: MobileRole.businessOwner,
            businessId: 8,
            outletName: null,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.scrollUntilVisible(
      find.text('Category demand'),
      240,
      scrollable: _verticalScrollable,
    );
    expect(find.text('Category demand'), findsOneWidget);
    expect(find.text('Product demand'), findsOneWidget);
    expect(find.text('Colour demand'), findsOneWidget);
    expect(find.text('Size demand'), findsOneWidget);
    expect(find.text('Black remains strongest'), findsNothing);
    expect(find.textContaining('No ranked products'), findsOneWidget);

    await tester.drag(find.byType(ListView).first, const Offset(0, -500));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Colour'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Colour demand is unavailable'), findsOneWidget);
  });

  testWidgets('chart distinguishes actual, predictions and uncertainty', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ForecastChartPresentation(
            points: [
              ForecastChartPoint(
                periodStartUtc: DateTime.utc(2026, 1, 1),
                actual: 10,
              ),
              ForecastChartPoint(
                periodStartUtc: DateTime.utc(2026, 1, 2),
                actual: 12,
              ),
              ForecastChartPoint(
                periodStartUtc: DateTime.utc(2026, 1, 3),
                predicted: 14,
                lowerBound: 11,
                upperBound: 18,
              ),
            ],
          ),
        ),
      ),
    );

    expect(find.text('Observed actual'), findsOneWidget);
    expect(find.text('Model prediction'), findsOneWidget);
    expect(find.text('Uncertainty range'), findsOneWidget);
    expect(
      find.bySemanticsLabel(
        'Forecast chart. 2 observed actual points, '
        '1 model prediction points, and 1 uncertainty ranges. '
        'Values are shown as provided; no predictions are calculated on this device.',
      ),
      findsOneWidget,
    );
    expect(find.byType(CustomPaint), findsAtLeastNWidgets(1));
    semantics.dispose();
  });

  testWidgets('metadata panel exposes forecast provenance and quality', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ForecastMetadataPanel(
            metadata: ForecastMetadata(
              generatedAtUtc: DateTime.utc(2026, 10, 14, 12),
              modelVersion: 'synthetic-test-model',
              trainingCutoffUtc: DateTime.utc(2026, 10, 1),
              historyCoverageLabel: 'synthetic test period',
              quality: ForecastQualityState.validated,
              coverage: ForecastCoverageState.sufficient,
              usesFallback: false,
            ),
          ),
        ),
      ),
    );

    expect(
      find.textContaining('Generated: 2026-10-14T12:00:00'),
      findsOneWidget,
    );
    expect(find.text('Model version: synthetic-test-model'), findsOneWidget);
    expect(find.textContaining('Training cutoff:'), findsOneWidget);
    expect(find.text('Data quality: Validated'), findsOneWidget);
    expect(find.text('Coverage: Coverage assessed'), findsOneWidget);
    expect(find.text('Fallback model: No'), findsOneWidget);
  });

  testWidgets('explanation separates facts from optional AI narrative', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SalesForecastPage(
            role: MobileRole.businessOwner,
            businessId: 8,
            outletName: null,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.byKey(const ValueKey('forecast-section-selector')),
      240,
      scrollable: _verticalScrollable,
    );
    await tester.tap(find.text('Explanation'));
    await tester.pumpAndSettle();

    expect(find.text('Forecast facts unavailable'), findsOneWidget);
    expect(find.text('Optional AI explanation'), findsOneWidget);
    expect(find.textContaining('Not generated.'), findsOneWidget);
    expect(find.text('Insufficient-data state'), findsOneWidget);
    expect(find.text('Generated: unavailable'), findsOneWidget);
  });

  testWidgets('what-if assumptions remain local and non-executing', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SalesForecastPage(
            role: MobileRole.businessOwner,
            businessId: 8,
            outletName: null,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.byKey(const ValueKey('forecast-section-selector')),
      240,
      scrollable: _verticalScrollable,
    );
    await tester.tap(find.text('Recommendations'));
    await tester.pumpAndSettle();

    expect(find.text('Recommendation unavailable'), findsOneWidget);
    expect(find.text('Safety stock assumption'), findsOneWidget);
    expect(find.text('Lead-time assumption'), findsOneWidget);
    expect(find.text('Proposed units assumption'), findsOneWidget);
    expect(find.text('Simulation not connected'), findsOneWidget);
    expect(find.text('Create purchase order'), findsNothing);

    await tester.enterText(
      find.widgetWithText(TextField, 'Safety stock assumption'),
      '17',
    );
    await tester.pumpAndSettle();
    expect(find.text('17'), findsOneWidget);
    expect(
      find.text(
        'No purchase order, production request or stock adjustment can be created here.',
      ),
      findsOneWidget,
    );
  });

  testWidgets('compact screen and large text remain usable', (tester) async {
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
            body: SalesForecastPage(
              role: MobileRole.businessOwner,
              businessId: 8,
              outletName: null,
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.text('Sales Forecasting'), findsOneWidget);
  });
}

final _verticalScrollable = find
    .byWidgetPredicate(
      (widget) =>
          widget is Scrollable && widget.axisDirection == AxisDirection.down,
    )
    .first;
