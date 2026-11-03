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
  testWidgets('cashier gets role-specific tabs and profile context', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final repository = FakeSessionRepository(
      restoreContext: _sessionFor('Cashier', isBusinessOwner: false),
    );
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionRepositoryProvider.overrideWithValue(repository),
          erpBaseUriProvider.overrideWithValue(Uri.parse('https://erp.test/')),
        ],
        child: const IraniMartApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(repository.restoreCalls, 1);
    expect(find.text('My dashboard'), findsOneWidget);
    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.text('Home'), findsOneWidget);
    final primaryNavigation = find.byType(NavigationBar);
    expect(
      find.descendant(of: primaryNavigation, matching: find.text('Stock Take')),
      findsOneWidget,
    );
    expect(
      find.descendant(of: primaryNavigation, matching: find.text('Alerts')),
      findsOneWidget,
    );
    expect(
      find.descendant(of: primaryNavigation, matching: find.text('Profile')),
      findsOneWidget,
    );
    expect(find.text('Insights'), findsNothing);

    await tester.tap(find.byTooltip('More sections'));
    await tester.pumpAndSettle();
    expect(find.text('Insights'), findsOneWidget);
    await tester.tap(find.text('Insights'));
    await tester.pumpAndSettle();
    expect(find.text('Personal reporting scope'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.text('My sales history'),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('My sales history'), findsOneWidget);

    await tester.tap(find.text('Profile'));
    await tester.pumpAndSettle();
    expect(find.text('A User'), findsOneWidget);
    expect(find.text('user@example.test'), findsOneWidget);
    expect(find.text('Business 8'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
  });

  testWidgets('manager cannot open the owner-only forecast destination', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(
            restoreContext: _sessionFor('Manager', isBusinessOwner: false),
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

    expect(find.byType(NavigationRail), findsOneWidget);
    await tester.tap(find.byTooltip('More sections'));
    await tester.pumpAndSettle();
    expect(find.text('Forecast'), findsNothing);
    await tester.tapAt(const Offset(20, 20));
    await tester.pumpAndSettle();

    container.read(appRouterProvider).go('/forecast');
    await tester.pumpAndSettle();
    expect(find.text('Your account'), findsOneWidget);
    expect(find.text('Not connected'), findsNothing);
  });

  testWidgets('logout returns to sign-in without losing logout notice', (
    tester,
  ) async {
    final repository = FakeSessionRepository(
      restoreContext: sampleSessionContext,
    )..logoutResult = const LogoutResult(remoteRevocationConfirmed: false);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionRepositoryProvider.overrideWithValue(repository),
          erpBaseUriProvider.overrideWithValue(Uri.parse('https://erp.test/')),
        ],
        child: const IraniMartApp(),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Sign out'));
    await tester.pumpAndSettle();
    expect(find.text('Sign in'), findsOneWidget);
    expect(
      find.textContaining('remote token revocation could not be confirmed'),
      findsOneWidget,
    );
  });

  testWidgets('manager can open settings and sign out through existing flow', (
    tester,
  ) async {
    final repository = FakeSessionRepository(
      restoreContext: _sessionFor('Manager', isBusinessOwner: false),
    )..logoutResult = const LogoutResult(remoteRevocationConfirmed: false);
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

    container.read(appRouterProvider).go('/settings');
    await tester.pumpAndSettle();
    expect(find.text('Settings'), findsOneWidget);
    expect(find.text('Staging'), findsNothing);

    await tester.tap(find.byTooltip('Sign out'));
    await tester.pumpAndSettle();
    expect(find.text('Sign in'), findsOneWidget);
    expect(
      find.textContaining('remote token revocation could not be confirmed'),
      findsOneWidget,
    );
    expect(repository.signOutCalls, 1);
  });

  testWidgets('owner routes remain responsive at phone and wide sizes', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(
            restoreContext: _sessionFor(
              'Business Owner',
              isBusinessOwner: true,
            ),
          ),
        ),
        erpBaseUriProvider.overrideWithValue(Uri.parse('https://erp.test/')),
      ],
    );
    addTearDown(container.dispose);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(
      tester.binding.platformDispatcher.clearTextScaleFactorTestValue,
    );

    for (final width in [320.0, 360.0, 390.0, 430.0, 840.0]) {
      tester.view.physicalSize = Size(width, 800);
      tester.view.devicePixelRatio = 1;
      tester.binding.platformDispatcher.textScaleFactorTestValue = 2;
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const IraniMartApp(),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull, reason: 'Dashboard at $width dp');

      for (final location in [
        '/dashboard',
        '/stock-take',
        '/stock-take?section=create',
        '/stock-take?section=count',
        '/stock-take?section=synchronization',
        '/stock-take?section=review',
        '/stock-take?section=reconciliation',
        '/stock-take?section=history',
        '/insights',
        '/catalogue',
        '/notifications',
        '/forecast',
        '/profile',
        '/settings',
      ]) {
        container.read(appRouterProvider).go(location);
        await tester.pump(const Duration(milliseconds: 100));
        await tester.pump(const Duration(milliseconds: 100));
        expect(
          tester.takeException(),
          isNull,
          reason: '$location at $width dp with 2x text',
        );
      }
    }
  });

  testWidgets('unsupported role gets the unauthorized profile state', (
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

    expect(find.text('Mobile dashboard unavailable'), findsOneWidget);
    expect(find.text('Your account'), findsOneWidget);
    expect(find.text('Home'), findsNothing);
    expect(find.text('Profile'), findsOneWidget);

    container.read(appRouterProvider).go('/dashboard');
    await tester.pumpAndSettle();
    expect(find.text('Mobile dashboard unavailable'), findsOneWidget);
  });
}

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
