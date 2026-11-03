enum SettingsSection {
  notifications('Notification preferences'),
  scanner('Scanner preferences'),
  offline('Offline catalogue and synchronization'),
  session('Pending operations and session'),
  about('App and support');

  const SettingsSection(this.title);

  final String title;
}

enum SettingsAvailability {
  available('Available'),
  requiresBackend('Requires ERP support'),
  unavailable('Not available in this release');

  const SettingsAvailability(this.label);

  final String label;
}

enum SettingsControl { information, disabledSwitch }

final class SettingsItem {
  const SettingsItem({
    required this.title,
    required this.description,
    required this.availability,
    this.control = SettingsControl.information,
  });

  final String title;
  final String description;
  final SettingsAvailability availability;
  final SettingsControl control;
}

final class SettingsPresentationModel {
  const SettingsPresentationModel({required this.environment});

  static const version = '1.0.0+1';

  final String environment;

  List<SettingsItem> itemsFor(SettingsSection section) => switch (section) {
    SettingsSection.notifications => const [
      SettingsItem(
        title: 'Notification delivery preferences',
        description: 'Preferences will be available when the ERP notification settings contract is connected.',
        availability: SettingsAvailability.requiresBackend,
        control: SettingsControl.disabledSwitch,
      ),
    ],
    SettingsSection.scanner => const [
      SettingsItem(
        title: 'Sound feedback',
        description: 'Barcode scanning is not implemented, so sound behavior cannot be changed.',
        availability: SettingsAvailability.unavailable,
        control: SettingsControl.disabledSwitch,
      ),
      SettingsItem(
        title: 'Vibration feedback',
        description: 'Barcode scanning is not implemented, so vibration behavior cannot be changed.',
        availability: SettingsAvailability.unavailable,
        control: SettingsControl.disabledSwitch,
      ),
    ],
    SettingsSection.offline => const [
      SettingsItem(
        title: 'Catalogue status',
        description:
            'Offline catalogue storage and synchronization are not connected.',
        availability: SettingsAvailability.unavailable,
      ),
      SettingsItem(
        title: 'Last successful synchronization',
        description: 'Unavailable. No synchronization timestamp or server state is reported.',
        availability: SettingsAvailability.unavailable,
      ),
    ],
    SettingsSection.session => const [
      SettingsItem(
        title: 'Pending offline operations',
        description: 'Unavailable. Offline operations are not implemented; no pending count is available.',
        availability: SettingsAvailability.unavailable,
      ),
      SettingsItem(
        title: 'Sign out',
        description: 'Use Sign out in the app bar. The existing session flow clears local credentials and reports unconfirmed server revocation.',
        availability: SettingsAvailability.available,
      ),
    ],
    SettingsSection.about => const [
      SettingsItem(
        title: 'App version',
        description: 'Version ${SettingsPresentationModel.version}',
        availability: SettingsAvailability.available,
      ),
      SettingsItem(
        title: 'Support',
        description:
            'For account or outlet access, contact your ERP administrator.',
        availability: SettingsAvailability.available,
      ),
    ],
  };
}
