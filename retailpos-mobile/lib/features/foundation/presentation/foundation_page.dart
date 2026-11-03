import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iranimart_business_mobile/features/foundation/application/foundation_status_provider.dart';
import 'package:iranimart_business_mobile/features/foundation/domain/foundation_status.dart';

class FoundationPage extends ConsumerWidget {
  const FoundationPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final status = ref.watch(foundationStatusProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Foundation status')),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text('Environment: ${status.environment}'),
          const SizedBox(height: 16),
          for (final capability in FoundationCapability.values)
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(_capabilityLabel(capability)),
              trailing: Text(
                status.isEnabled(capability) ? 'Enabled' : 'Not enabled',
              ),
            ),
          const SizedBox(height: 8),
          const Text(
            'This foundation build does not authenticate or access ERP business data.',
          ),
        ],
      ),
    );
  }

  String _capabilityLabel(FoundationCapability capability) =>
      switch (capability) {
        FoundationCapability.authentication => 'Authentication',
        FoundationCapability.erpBusinessData => 'ERP business data',
        FoundationCapability.localCatalogue => 'Offline catalogue',
        FoundationCapability.stockTake => 'Stock take',
      };
}
