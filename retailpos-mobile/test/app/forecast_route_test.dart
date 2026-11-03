import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/iranimart_app.dart';
import 'package:iranimart_business_mobile/app/router/app_router.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_providers.dart';

import '../features/auth/auth_test_fakes.dart';

SessionContext _forecastSession(String role) => SessionContext(
  profile: UserProfile(
    id: 4,
    businessId: 8,
    name: 'A User',
    email: 'user@example.test',
    roleName: role,
    isRoleSwitched: false,
    isBusinessOwner: role == 'Business Owner',
    mustResetPassword: false,
  ),
  outlets: const [AuthorizedOutlet(id: 21, name: 'North Outlet')],
  selectedOutletId: 21,
);

void main() {
  testWidgets('business owner opens forecast with ERP session context', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(
            restoreContext: _forecastSession('Business Owner'),
          ),
        ),
        erpBaseUriProvider.overrideWithValue(Uri.parse('https://erp.test/')),
      ],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: const IraniMartApp(),
      ),
    );
    await tester.pumpAndSettle();
    container.read(appRouterProvider).go('/forecast');
    await tester.pumpAndSettle();

    expect(find.text('Sales Forecasting'), findsOneWidget);
    expect(find.text('Business 8'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
    expect(find.text('Forecast unavailable'), findsOneWidget);
  });

  testWidgets('manager cannot access owner-only forecast route', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(restoreContext: _forecastSession('Manager')),
        ),
        erpBaseUriProvider.overrideWithValue(Uri.parse('https://erp.test/')),
      ],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: const IraniMartApp(),
      ),
    );
    await tester.pumpAndSettle();
    container.read(appRouterProvider).go('/forecast');
    await tester.pumpAndSettle();

    expect(find.text('Your account'), findsOneWidget);
    expect(find.text('Sales Forecasting'), findsNothing);
  });
}
