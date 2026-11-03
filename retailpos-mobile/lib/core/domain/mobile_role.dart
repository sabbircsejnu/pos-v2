enum MobileRole {
  businessOwner('Business Owner'),
  manager('Manager'),
  salesperson('Cashier / Salesperson'),
  unsupported('Role not available');

  const MobileRole(this.label);

  final String label;

  static MobileRole fromServerProfile({
    required String? roleName,
    required bool isBusinessOwner,
  }) {
    if (isBusinessOwner) return MobileRole.businessOwner;

    final normalized = roleName?.toLowerCase().replaceAll(
      RegExp('[^a-z0-9]'),
      '',
    );
    return switch (normalized) {
      'businessowner' || 'owner' => MobileRole.businessOwner,
      'manager' || 'salesmanager' || 'branchmanager' => MobileRole.manager,
      'cashier' || 'salesperson' || 'salesassociate' => MobileRole.salesperson,
      _ => MobileRole.unsupported,
    };
  }
}
