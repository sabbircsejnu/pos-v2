import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/presentation/profile_page.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_models.dart';

import '../features/auth/auth_test_fakes.dart';

void main() {
  testWidgets(
    'profile shows ERP identity and selects only authorized outlets',
    (tester) async {
      final repository = FakeSessionRepository(
        restoreContext: sampleSessionContext,
      );
      final container = ProviderContainer(
        overrides: [sessionRepositoryProvider.overrideWithValue(repository)],
      );
      addTearDown(container.dispose);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const MaterialApp(home: Scaffold(body: ProfilePage())),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('A User'), findsOneWidget);
      expect(find.text('user@example.test'), findsOneWidget);
      expect(find.text('Business Owner'), findsOneWidget);
      expect(find.text('Business 8'), findsOneWidget);
      expect(find.text('North Outlet'), findsOneWidget);
      expect(find.text('South Outlet'), findsNothing);
      expect(find.text('Role switching'), findsNothing);

      await tester.tap(
        find.byKey(const ValueKey('authorized-outlet-selector')),
      );
      await tester.pumpAndSettle();
      expect(find.text('South Outlet'), findsOneWidget);
      await tester.tap(find.text('South Outlet').last);
      await tester.pumpAndSettle();

      expect(
        container.read(authSessionControllerProvider).context?.selectedOutletId,
        22,
      );
      expect(find.text('Current context: South Outlet'), findsOneWidget);
    },
  );

  testWidgets('missing or unauthorized outlet context is not exposed', (
    tester,
  ) async {
    final context = SessionContext(
      profile: sampleSessionContext.profile,
      outlets: const [AuthorizedOutlet(id: 21, name: 'Authorized Outlet')],
      selectedOutletId: 99,
    );
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(
          FakeSessionRepository(restoreContext: context),
        ),
      ],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: const MaterialApp(home: Scaffold(body: ProfilePage())),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Selected outlet unavailable'), findsOneWidget);
    expect(find.text('South Outlet'), findsNothing);
    expect(find.textContaining('99'), findsNothing);
    await tester.tap(find.byKey(const ValueKey('authorized-outlet-selector')));
    await tester.pumpAndSettle();
    expect(find.text('Authorized Outlet'), findsOneWidget);
    expect(find.text('South Outlet'), findsNothing);
  });

  testWidgets('profile reports unavailable when there is no ERP session', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [
        sessionRepositoryProvider.overrideWithValue(FakeSessionRepository()),
      ],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: const MaterialApp(home: Scaffold(body: ProfilePage())),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Profile unavailable'), findsOneWidget);
    expect(find.textContaining('Sign in again'), findsOneWidget);
    expect(find.byType(DropdownButtonFormField<int>), findsNothing);
  });
}
