import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/config/app_environment.dart';
import 'package:iranimart_business_mobile/features/settings/domain/settings_presentation_models.dart';
import 'package:iranimart_business_mobile/features/settings/presentation/settings_page.dart';

void main() {
  testWidgets('settings distinguishes available and disconnected controls', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: SettingsPage(environment: AppEnvironment.staging)),
      ),
    );

    expect(find.text('Notification preferences'), findsOneWidget);
    expect(find.text('Scanner preferences'), findsOneWidget);
    expect(find.text('Offline catalogue and synchronization'), findsOneWidget);
    expect(find.text('Pending operations and session'), findsOneWidget);
    expect(find.text('App and support'), findsOneWidget);
    expect(find.textContaining('Version 1.0.0+1'), findsOneWidget);
    expect(find.textContaining('Staging'), findsOneWidget);

    final switches = tester.widgetList<SwitchListTile>(
      find.byType(SwitchListTile),
    );
    expect(switches, hasLength(3));
    expect(switches.every((tile) => tile.onChanged == null), isTrue);
    expect(find.textContaining('Requires ERP support'), findsOneWidget);
    expect(
      find.textContaining('Not available in this release'),
      findsNWidgets(5),
    );
    expect(
      find.textContaining('Last successful synchronization'),
      findsOneWidget,
    );
    expect(
      find.textContaining('no pending count is available'),
      findsOneWidget,
    );
    expect(find.textContaining('No settings were changed'), findsOneWidget);

    final previewWarning = find.text('Preview future sign-out warning');
    await tester.ensureVisible(previewWarning);
    await tester.tap(previewWarning);
    await tester.pumpAndSettle();
    expect(find.text('Future session-exit confirmation'), findsOneWidget);
    expect(find.textContaining('no pending work is tracked'), findsOneWidget);
    await tester.tap(find.text('Close'));
    await tester.pumpAndSettle();
    expect(find.text('Future session-exit confirmation'), findsNothing);
  });

  testWidgets(
    'settings layout remains usable with large text on compact screen',
    (tester) async {
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
              body: SettingsPage(environment: AppEnvironment.development),
            ),
          ),
        ),
      );

      expect(find.text('Settings'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.ensureVisible(find.textContaining('Development'));
      expect(find.textContaining('Development'), findsOneWidget);
    },
  );

  test('settings model has typed status for unavailable preferences', () {
    const model = SettingsPresentationModel(environment: 'Development');
    final notification = model.itemsFor(SettingsSection.notifications).single;
    final scanner = model.itemsFor(SettingsSection.scanner);

    expect(notification.availability, SettingsAvailability.requiresBackend);
    expect(notification.control, SettingsControl.disabledSwitch);
    expect(scanner, hasLength(2));
    expect(
      scanner.every(
        (item) =>
            item.availability == SettingsAvailability.unavailable &&
            item.control == SettingsControl.disabledSwitch,
      ),
      isTrue,
    );
    expect(model.itemsFor(SettingsSection.offline), hasLength(2));
    expect(model.itemsFor(SettingsSection.session), hasLength(2));
  });
}
