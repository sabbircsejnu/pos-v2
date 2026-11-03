import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../application/auth_session_controller.dart';
import 'session_loading_page.dart';

class AuthenticatedHomePage extends ConsumerWidget {
  const AuthenticatedHomePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    return Scaffold(
      appBar: AppBar(
        title: const Text('IraniMart Business'),
        actions: [
          IconButton(
            tooltip: 'Sign out',
            onPressed: () =>
                ref.read(authSessionControllerProvider.notifier).signOut(),
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: ListView(
            padding: const EdgeInsets.all(24),
            shrinkWrap: true,
            children: [
              Text(
                session.profile.name,
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: 4),
              Text(session.profile.roleName ?? 'ERP user'),
              const SizedBox(height: 8),
              Text('Business ${session.profile.businessId}'),
              const SizedBox(height: 24),
              if (session.outlets.isEmpty)
                const Text('No authorized outlets are available.')
              else if (session.outlets.length == 1)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Authorized outlet'),
                  subtitle: Text(session.outlets.single.name),
                )
              else
                DropdownButtonFormField<int>(
                  initialValue: session.selectedOutletId,
                  decoration: const InputDecoration(
                    labelText: 'Authorized outlet',
                    border: OutlineInputBorder(),
                  ),
                  items: [
                    for (final outlet in session.outlets)
                      DropdownMenuItem(
                        value: outlet.id,
                        child: Text(outlet.name),
                      ),
                  ],
                  onChanged: (id) {
                    if (id != null) {
                      ref
                          .read(authSessionControllerProvider.notifier)
                          .selectOutlet(id);
                    }
                  },
                ),
              const SizedBox(height: 24),
              const Text(
                'Authenticated shell only. Business data is not enabled in this slice.',
              ),
            ],
          ),
        ),
      ),
    );
  }
}
