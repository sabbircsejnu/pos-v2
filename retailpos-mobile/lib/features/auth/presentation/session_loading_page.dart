import 'package:flutter/material.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

final class SessionLoadingPage extends StatelessWidget {
  const SessionLoadingPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: const ContentStatePanel(
                state: ContentState.loading,
                title: 'Restoring secure session',
                message: 'Verifying your profile and authorized outlets.',
              ),
            ),
          ),
        ),
      ),
    );
  }
}
