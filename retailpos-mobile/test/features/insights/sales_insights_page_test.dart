import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/insights/presentation/sales_insights_page.dart';

void main() {
  testWidgets('owner sees business-wide insights without prototype values', (
    tester,
  ) async {
    await _pumpInsights(tester, MobileRole.businessOwner);

    expect(find.text('Insights'), findsOneWidget);
    expect(find.text('Business context'), findsOneWidget);
    await _scrollIntoView(tester, 'Business sales trend');
    expect(find.text('Outlet performance'), findsOneWidget);
    expect(find.text('Category performance'), findsOneWidget);
    expect(find.text('Top-performing products'), findsOneWidget);
    expect(find.text('Product and variant demand'), findsOneWidget);
    expect(find.text('Discount distribution'), findsOneWidget);
    expect(find.text('Margin-sensitive insights'), findsOneWidget);
    for (final prototypeValue in ['৳18.4L', '2,840', '16.2%', '48']) {
      expect(find.textContaining(prototypeValue), findsNothing);
    }
  });

  testWidgets('manager insights identify the selected outlet and omit margin', (
    tester,
  ) async {
    await _pumpInsights(tester, MobileRole.manager);

    expect(find.text('Authorized outlet context'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
    await _scrollIntoView(tester, 'Outlet sales trend');
    expect(find.text('Product and variant demand'), findsOneWidget);
    expect(find.text('Sales performance'), findsOneWidget);
    expect(find.text('Discount insights'), findsOneWidget);
    expect(find.text('Margin-sensitive insights'), findsNothing);
    expect(find.text('Outlet performance'), findsNothing);
  });

  testWidgets('frontline insights are self-only and remain provisional', (
    tester,
  ) async {
    await _pumpInsights(tester, MobileRole.salesperson);

    expect(find.text('Personal reporting scope'), findsOneWidget);
    expect(
      find.textContaining(
        'Cashier-specific policy and layout remain provisional',
      ),
      findsOneWidget,
    );
    await _scrollIntoView(tester, 'My sales history');
    expect(find.text('My product insights'), findsOneWidget);
    expect(find.text('My activity'), findsOneWidget);
    expect(find.text('Outlet performance'), findsNothing);
    expect(find.text('Discount insights'), findsNothing);
    expect(find.text('Margin-sensitive insights'), findsNothing);
  });

  testWidgets('period controls update presentation selection only', (
    tester,
  ) async {
    await _pumpInsights(tester, MobileRole.manager);

    await _scrollIntoView(tester, 'Reporting period');
    expect(find.textContaining('Selected: 7 Days.'), findsOneWidget);
    await tester.tap(find.text('30 Days'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Selected: 30 Days.'), findsOneWidget);
    expect(
      find.textContaining(
        'Reporting data and custom date selection are not connected.',
      ),
      findsOneWidget,
    );

    await tester.tap(find.text('Custom'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Selected: Custom.'), findsOneWidget);
    expect(find.textContaining('no reporting request is made'), findsOneWidget);
  });

  for (final role in [
    MobileRole.businessOwner,
    MobileRole.manager,
    MobileRole.salesperson,
  ]) {
    testWidgets('$role insights fit a compact screen with large text', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(360, 640);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        MaterialApp(
          home: MediaQuery(
            data: const MediaQueryData(
              size: Size(360, 640),
              textScaler: TextScaler.linear(2),
            ),
            child: Scaffold(
              body: SalesInsightsPage(
                role: role,
                profileName: 'A User',
                businessId: 8,
                outletName: 'North Outlet',
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await _scrollIntoView(tester, 'Reporting period');
      expect(tester.takeException(), isNull);
      expect(find.byType(SegmentedButton<InsightsPeriod>), findsOneWidget);
    });
  }
}

Future<void> _pumpInsights(WidgetTester tester, MobileRole role) async {
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: SalesInsightsPage(
          role: role,
          profileName: 'A User',
          businessId: 8,
          outletName: 'North Outlet',
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> _scrollIntoView(WidgetTester tester, String label) async {
  await tester.scrollUntilVisible(
    find.text(label),
    300,
    scrollable: find.byType(Scrollable).first,
  );
  await tester.pumpAndSettle();
}
