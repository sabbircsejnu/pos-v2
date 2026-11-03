import 'package:flutter/material.dart';

import '../../../core/config/app_environment.dart';
import '../../../shared/design_system/content_state_panel.dart';
import '../domain/settings_presentation_models.dart';

final class SettingsPage extends StatelessWidget {
  const SettingsPage({required this.environment, super.key});

  final AppEnvironment environment;

  @override
  Widget build(BuildContext context) {
    final model = SettingsPresentationModel(
      environment: switch (environment) {
        AppEnvironment.development => 'Development',
        AppEnvironment.staging => 'Staging',
      },
    );

    return SingleChildScrollView(
      key: const ValueKey('settings-page'),
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Settings', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 4),
          const Text(
            'Settings that are not connected or persisted are identified below.',
          ),
          const SizedBox(height: 16),
          for (final section in SettingsSection.values) ...[
            _SettingsSectionCard(
              section: section,
              items: model.itemsFor(section),
              environment: model.environment,
            ),
            const SizedBox(height: 12),
          ],
          const ContentStatePanel(
            state: ContentState.preview,
            title: 'No settings were changed',
            message: 'Preferences requiring local persistence or ERP support remain unavailable. No notification, catalogue, sync, or pending-operation data was requested.',
          ),
        ],
      ),
    );
  }
}

final class _SettingsSectionCard extends StatelessWidget {
  const _SettingsSectionCard({
    required this.section,
    required this.items,
    required this.environment,
  });

  final SettingsSection section;
  final List<SettingsItem> items;
  final String environment;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(section.title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          for (final item in items) ...[
            _SettingsItemTile(item: item),
            if (item != items.last) const Divider(height: 20),
          ],
          if (section == SettingsSection.about) ...[
            const Divider(height: 20),
            _SettingsItemTile(
              item: SettingsItem(
                title: 'Build environment',
                description: environment,
                availability: SettingsAvailability.available,
              ),
            ),
          ],
          if (section == SettingsSection.session) ...[
            const SizedBox(height: 8),
            const ContentStatePanel(
              state: ContentState.unavailable,
              title: 'Offline operation warning',
              message: 'Offline operations are not supported in this release. Once they exist, sign-out and account switching must warn or block when unresolved work remains.',
            ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: () => showDialog<void>(
                context: context,
                builder: (context) => AlertDialog(
                  title: const Text('Future session-exit confirmation'),
                  content: const Text(
                    'When offline operations are supported, this step should '
                    'warn before leaving unresolved work. This is a preview '
                    'only: offline operations are not implemented and no '
                    'pending work is tracked.',
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.of(context).pop(),
                      child: const Text('Close'),
                    ),
                  ],
                ),
              ),
              icon: const Icon(Icons.info_outline),
              label: const Text('Preview future sign-out warning'),
            ),
          ],
        ],
      ),
    ),
  );
}

final class _SettingsItemTile extends StatelessWidget {
  const _SettingsItemTile({required this.item});

  final SettingsItem item;

  @override
  Widget build(BuildContext context) {
    final isAvailable = item.availability == SettingsAvailability.available;
    return switch (item.control) {
      SettingsControl.information => ListTile(
        contentPadding: EdgeInsets.zero,
        title: Text(item.title),
        subtitle: Text('${item.description}\n${item.availability.label}'),
        isThreeLine: true,
        trailing: isAvailable
            ? const Icon(Icons.check_circle_outline)
            : const Icon(Icons.lock_outline),
      ),
      SettingsControl.disabledSwitch => SwitchListTile(
        contentPadding: EdgeInsets.zero,
        value: false,
        onChanged: null,
        title: Text(item.title),
        subtitle: Text('${item.description}\n${item.availability.label}'),
      ),
    };
  }
}
