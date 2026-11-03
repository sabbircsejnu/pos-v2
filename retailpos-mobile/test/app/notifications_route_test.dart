import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/iranimart_app.dart';
import 'package:iranimart_business_mobile/app/router/app_router.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_providers.dart';

import '../features/auth/auth_test_fakes.dart';

SessionContext _sessionFor(String roleName, {required bool isBusinessOwner}) =>
    SessionContext(
      profile: UserProfile(
        id: 4,
        businessId: 8,
        name: 'A User',
        email: 'user@example.test',
        roleName: roleName,
        isRoleSwitched: false,
        isBusinessOwner: isBusinessOwner,
        mustResetPassword: false,
      ),
      outlets: const [
        AuthorizedOutlet(id: 21, name: 'North Outlet'),
        AuthorizedOutlet(id: 22, name: 'South Outlet'),
      ],
      selectedOutletId: 21,
    );

void main() {
  testWidgets('salesperson opens inbox with session context through bell', (
    tester,
  ) async {
    final repository = FakeSessionRepository(
      restoreContext: _sessionFor('Salesperson', isBusinessOwner: false),
    );
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(repository),
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

    expect(
      find.byTooltip('Notifications · unread count unavailable'),
      findsOneWidget,
    );
    await tester.tap(
      find.byTooltip('Notifications · unread count unavailable'),
    );
    await tester.pumpAndSettle();

    expect(find.text('Notifications'), findsOneWidget);
    expect(find.text('Business 8'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
    expect(find.text('Inbox not connected'), findsOneWidget);
    expect(repository.restoreCalls, 1);
  });

  testWidgets('unsupported role cannot open notifications by direct route', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(
            restoreContext: _sessionFor('System Admin', isBusinessOwner: false),
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

    container.read(appRouterProvider).go('/notifications');
    await tester.pumpAndSettle();

    expect(find.text('Your account'), findsOneWidget);
    expect(find.text('Inbox not connected'), findsNothing);
  });
}
