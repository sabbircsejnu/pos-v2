import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/iranimart_app.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_providers.dart';

import '../features/auth/auth_test_fakes.dart';

void main() {
  testWidgets(
    'owner opens catalogue with authenticated business and outlet context',
    (tester) async {
      final repository = FakeSessionRepository(
        restoreContext: sampleSessionContext,
      );
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

      await tester.tap(find.byTooltip('More sections'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Catalogue'));
      await tester.pumpAndSettle();

      expect(find.text('Product catalogue'), findsOneWidget);
      expect(find.text('Business 8'), findsOneWidget);
      expect(find.text('North Outlet'), findsOneWidget);
      expect(find.text('Catalogue not connected'), findsOneWidget);
      expect(repository.restoreCalls, 1);
    },
  );
}
