import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

void main() {
  for (final state in ContentState.values) {
    testWidgets('$state state renders its message accessibly', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ContentStatePanel(
              state: state,
              title: 'State title',
              message: 'State detail',
            ),
          ),
        ),
      );

      expect(find.text('State title'), findsOneWidget);
      expect(find.text('State detail'), findsOneWidget);
      if (state == ContentState.loading) {
        expect(find.byType(CircularProgressIndicator), findsOneWidget);
      }
    });
  }
}
