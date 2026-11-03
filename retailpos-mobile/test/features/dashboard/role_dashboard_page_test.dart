import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/dashboard/presentation/role_dashboard_page.dart';

void main() {
  testWidgets('owner dashboard presents business-wide disconnected sections', (
    tester,
  ) async {
    await _pumpDashboard(tester, MobileRole.businessOwner);

    expect(find.text('Business overview'), findsOneWidget);
    expect(find.textContaining('not loaded or estimated'), findsOneWidget);
    expect(find.textContaining('56.4L'), findsNothing);
    await _scrollIntoView(tester, 'Sales summary');
    expect(find.text('Sales summary'), findsOneWidget);
    expect(find.text('Sales trends'), findsOneWidget);
    expect(find.text('Outlet performance'), findsOneWidget);
    expect(find.text('Top-performing products'), findsOneWidget);
    expect(find.text('Discount and margin insights'), findsOneWidget);
    expect(find.text('Stock alerts'), findsOneWidget);
    expect(find.text('Not connected'), findsNWidgets(6));
  });

  testWidgets('manager dashboard stays outlet-scoped and omits forecasting', (
    tester,
  ) async {
    await _pumpDashboard(tester, MobileRole.manager);

    expect(find.text('Manager dashboard'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
    await _scrollIntoView(tester, 'Assigned-outlet summary');
    expect(find.text('Assigned-outlet summary'), findsOneWidget);
    expect(find.text('Sales versus target'), findsOneWidget);
    expect(find.text('Staff performance'), findsOneWidget);
    expect(find.text('Stock Take status'), findsOneWidget);
    expect(find.text('Forecast'), findsNothing);
    expect(find.text('৳56.4L'), findsNothing);
  });

  testWidgets('frontline dashboard is clearly provisional and self-scoped', (
    tester,
  ) async {
    await _pumpDashboard(tester, MobileRole.salesperson);

    expect(find.text('My dashboard'), findsOneWidget);
    expect(
      find.textContaining('share this provisional frontline layout'),
      findsOneWidget,
    );
    expect(find.text('North Outlet'), findsOneWidget);
    await _scrollIntoView(tester, 'My sales activity');
    expect(find.text('My sales activity'), findsOneWidget);
    expect(find.text('Target progress'), findsOneWidget);
    expect(find.text('Invoices and units'), findsOneWidget);
    expect(find.text('Outlet and tasks'), findsOneWidget);
    expect(find.text('Discount and margin insights'), findsNothing);
  });

  testWidgets('quick actions use completed destination labels', (tester) async {
    await _pumpDashboard(tester, MobileRole.manager);

    expect(find.widgetWithText(OutlinedButton, 'Stock Take'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Insights'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Catalogue'), findsOneWidget);
    expect(find.text('Stock Take preview'), findsNothing);
    expect(find.text('Insights preview'), findsNothing);
    expect(find.text('Catalogue preview'), findsNothing);
  });

  for (final role in [
    MobileRole.businessOwner,
    MobileRole.manager,
    MobileRole.salesperson,
  ]) {
    testWidgets('$role dashboard adapts to small screens and large text', (
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
              body: RoleDashboardPage(
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

      await _scrollIntoView(tester, 'Preview only');
      expect(tester.takeException(), isNull);
      expect(find.text('Preview only'), findsOneWidget);
    });
  }
}

Future<void> _pumpDashboard(WidgetTester tester, MobileRole role) async {
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: RoleDashboardPage(
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
