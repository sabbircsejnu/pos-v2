import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/iranimart_app.dart';
import 'package:iranimart_business_mobile/app/router/app_router.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_providers.dart';

import '../features/auth/auth_test_fakes.dart';

void main() {
  testWidgets('authenticated Owner navigation opens Stock Take and sections', (
    tester,
  ) async {
    final repository = FakeSessionRepository(
      restoreContext: sampleSessionContext,
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

    container.read(appRouterProvider).go('/stock-take');
    await tester.pumpAndSettle();
    expect(find.text('Stock Take'), findsNWidgets(2));
    expect(find.text('Business 8'), findsOneWidget);
    expect(
      find.text('Authorized outlet context: North Outlet'),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const ValueKey('stock-take-section-selector')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Create stock take').last);
    await tester.pumpAndSettle();
    expect(find.text('Create Stock Take'), findsOneWidget);
    expect(find.text('Start session unavailable'), findsOneWidget);
    expect(
      container.read(appRouterProvider).routeInformationProvider.value.uri.path,
      '/stock-take',
    );
    expect(
      container
          .read(appRouterProvider)
          .routeInformationProvider
          .value
          .uri
          .queryParameters['section'],
      'create',
    );
    expect(repository.restoreCalls, 1);
  });

  testWidgets('unauthenticated Stock Take route redirects to sign-in', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(FakeSessionRepository()),
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

    container.read(appRouterProvider).go('/stock-take?section=count');
    await tester.pumpAndSettle();
    expect(find.text('Sign in'), findsOneWidget);
    expect(find.text('Scan and count'), findsNothing);
  });

  testWidgets('unsupported authenticated role cannot open Stock Take', (
    tester,
  ) async {
    final context = SessionContext(
      profile: const UserProfile(
        id: 4,
        businessId: 8,
        name: 'A User',
        email: 'user@example.test',
        roleName: 'System Admin',
        isRoleSwitched: false,
        isBusinessOwner: false,
        mustResetPassword: false,
      ),
      outlets: const [AuthorizedOutlet(id: 21, name: 'North Outlet')],
      selectedOutletId: 21,
    );
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(restoreContext: context),
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

    container.read(appRouterProvider).go('/stock-take?section=review');
    await tester.pumpAndSettle();
    expect(find.text('Your account'), findsOneWidget);
    expect(find.text('Stock Take'), findsNothing);
    expect(find.text('Count review'), findsNothing);
  });

  testWidgets('Stock Take does not expose an unauthorized selected outlet', (
    tester,
  ) async {
    final invalidContext = SessionContext(
      profile: sampleSessionContext.profile,
      outlets: const [AuthorizedOutlet(id: 21, name: 'Authorized Outlet')],
      selectedOutletId: 99,
    );
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(restoreContext: invalidContext),
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

    container.read(appRouterProvider).go('/stock-take');
    await tester.pumpAndSettle();
    expect(find.text('Selected outlet unavailable'), findsOneWidget);
    expect(find.text('Authorized Outlet'), findsNothing);
    expect(find.textContaining('99'), findsNothing);
  });
}
