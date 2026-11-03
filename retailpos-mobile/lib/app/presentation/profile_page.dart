import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_failure.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

final class ProfilePage extends ConsumerWidget {
  const ProfilePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final authState = ref.watch(authSessionControllerProvider);
    final session = authState.context;
    if (session == null) {
      final isCheckingSession =
          authState.status == AuthStatus.restoring ||
          authState.status == AuthStatus.signingIn;
      return ContentStatePanel(
        state: isCheckingSession
            ? ContentState.loading
            : ContentState.unauthorized,
        title: isCheckingSession ? 'Loading profile' : 'Profile unavailable',
        message: isCheckingSession ? 'Checking the authenticated session.' : 'A valid ERP session is required to view profile and outlet context. Sign in again.',
      );
    }

    final profile = session.profile;
    final role = MobileRole.fromServerProfile(
      roleName: profile.roleName,
      isBusinessOwner: profile.isBusinessOwner,
    );
    final roleName = profile.roleName?.trim();
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return ListView(
      key: const ValueKey('profile-page'),
      padding: const EdgeInsets.all(20),
      children: [
        if (role == MobileRole.unsupported) ...[
          const ContentStatePanel(
            state: ContentState.unauthorized,
            title: 'Mobile dashboard unavailable',
            message: 'This account role has no mobile dashboard destinations. Contact your administrator.',
          ),
          const SizedBox(height: 16),
        ],
        Text('Your account', style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 16),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const CircleAvatar(
                  radius: 28,
                  child: Icon(Icons.person_outline, size: 30),
                ),
                const SizedBox(height: 16),
                Text(
                  profile.name.isEmpty ? 'Name unavailable' : profile.name,
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 4),
                Text(
                  profile.email.isEmpty ? 'Email unavailable' : profile.email,
                ),
                const SizedBox(height: 12),
                Text(
                  role == MobileRole.unsupported
                      ? 'Role unavailable'
                      : roleName == null || roleName.isEmpty
                      ? role.label
                      : roleName,
                  key: const ValueKey('profile-role'),
                ),
                const SizedBox(height: 4),
                Text(
                  profile.businessId == null
                      ? 'Business context unavailable'
                      : 'Business ${profile.businessId}',
                  key: const ValueKey('profile-business'),
                ),
                const SizedBox(height: 8),
                const Text(
                  'Identity and access are provided by the signed-in ERP account.',
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 24),
        Text(
          'Authorized outlet',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 8),
        if (session.outlets.isEmpty)
          const ContentStatePanel(
            state: ContentState.empty,
            title: 'No authorized outlets',
            message: 'Ask your administrator to review your outlet access. Business data remains unavailable.',
          )
        else
          DropdownButtonFormField<int>(
            key: const ValueKey('authorized-outlet-selector'),
            initialValue: selectedOutlet?.id,
            decoration: const InputDecoration(
              labelText: 'Select outlet',
              border: OutlineInputBorder(),
            ),
            isExpanded: true,
            items: [
              for (final outlet in session.outlets)
                DropdownMenuItem(value: outlet.id, child: Text(outlet.name)),
            ],
            onChanged: (id) {
              if (id != null) {
                ref
                    .read(authSessionControllerProvider.notifier)
                    .selectOutlet(id);
              }
            },
          ),
        if (session.selectedOutletId != null && selectedOutlet == null) ...[
          const SizedBox(height: 12),
          const ContentStatePanel(
            state: ContentState.error,
            title: 'Selected outlet unavailable',
            message: 'The current outlet is not in the authorized outlet list. Select an authorized outlet or sign in again.',
          ),
        ] else if (selectedOutlet != null) ...[
          const SizedBox(height: 12),
          Text(
            'Current context: ${selectedOutlet.name}',
            key: const ValueKey('selected-outlet-context'),
          ),
        ],
        if (authState.failure == AuthFailureKind.forbidden) ...[
          const SizedBox(height: 12),
          const ContentStatePanel(
            state: ContentState.error,
            title: 'Outlet selection rejected',
            message: 'Choose an outlet from the authorized list.',
          ),
        ],
        const SizedBox(height: 24),
        const ContentStatePanel(
          state: ContentState.preview,
          title: 'Session and sign out',
          message: 'Your role and outlet access come from the ERP session. Use Sign out in the app bar to clear this device session; the app reports if remote revocation cannot be confirmed.',
        ),
      ],
    );
  }
}
