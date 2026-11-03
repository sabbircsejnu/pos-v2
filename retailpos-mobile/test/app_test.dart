import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/iranimart_app.dart';
import 'package:iranimart_business_mobile/app/router/app_router.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_providers.dart';

import 'features/auth/auth_test_fakes.dart';

void main() {
  testWidgets('sign-in form remains scrollable with large text and keyboard', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 300);
    tester.binding.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    addTearDown(
      tester.binding.platformDispatcher.clearTextScaleFactorTestValue,
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionRepositoryProvider.overrideWithValue(FakeSessionRepository()),
          erpBaseUriProvider.overrideWithValue(Uri.parse('https://erp.test/')),
        ],
        child: const IraniMartApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byType(TextFormField), findsNWidgets(2));
    await tester.ensureVisible(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.widgetWithText(FilledButton, 'Sign in'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets(
    'signed-out navigation redirects to sign-in and obscures password',
    (tester) async {
      final repository = FakeSessionRepository();
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

      expect(find.text('Sign in'), findsOneWidget);
      expect(find.byType(TextFormField), findsNWidgets(2));
      expect(
        tester.widget<EditableText>(find.byType(EditableText).last).obscureText,
        isTrue,
      );
      expect(find.bySemanticsLabel('IraniMart logo'), findsOneWidget);
      expect(find.text('Business'), findsOneWidget);
      expect(find.text('IRANIMART'), findsNothing);
      expect(find.text('BUSINESS'), findsNothing);
      for (final roleLabel in [
        'Owner',
        'Manager',
        'Salesperson',
        'Business Owner',
        'Sales Manager',
      ]) {
        expect(find.text(roleLabel), findsNothing, reason: roleLabel);
      }
      expect(find.byType(DropdownButton<String>), findsNothing);
      expect(find.byType(SegmentedButton<String>), findsNothing);
      expect(find.byType(RadioListTile<String>), findsNothing);
      expect(find.byType(ChoiceChip), findsNothing);

      container.read(appRouterProvider).go('/');
      await tester.pumpAndSettle();
      expect(find.text('Sign in'), findsOneWidget);
    },
  );

  testWidgets(
    'verified profile and permitted outlets reach the neutral shell',
    (tester) async {
      final repository = FakeSessionRepository(context: sampleSessionContext);
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionRepositoryProvider.overrideWithValue(repository),
            erpBaseUriProvider.overrideWithValue(
              Uri.parse('https://erp.test/'),
            ),
          ],
          child: const IraniMartApp(),
        ),
      );
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byType(TextFormField).at(0),
        'owner@example.test',
      );
      await tester.enterText(find.byType(TextFormField).at(1), 'not retained');
      await tester.ensureVisible(find.widgetWithText(FilledButton, 'Sign in'));
      await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
      await tester.pumpAndSettle();

      expect(find.text('Hello, A User'), findsOneWidget);
      expect(find.text('Business 8'), findsOneWidget);
      expect(find.text('North Outlet'), findsOneWidget);
      expect(find.text('Preview only'), findsOneWidget);
      expect(find.text('Business overview'), findsOneWidget);
      expect(repository.signInCalls, 1);
    },
  );

  testWidgets('logout reports when remote revocation could not be confirmed', (
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
      find.text(
        'Signed out on this device; remote token revocation could not be confirmed.',
      ),
      findsOneWidget,
    );
    expect(repository.signOutCalls, 1);
  });
}
