import 'package:flutter/material.dart';
import 'package:iranimart_business_mobile/app/navigation/role_navigation.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

final class FeaturePreviewPage extends StatelessWidget {
  const FeaturePreviewPage({required this.destination, super.key});

  final AppDestination destination;

  @override
  Widget build(BuildContext context) {
    final (icon, detail) = switch (destination) {
      AppDestination.stockTake => (
        Icons.inventory_2_outlined,
        'Catalogue sync, counting and stock-take sessions are not connected in this release.',
      ),
      AppDestination.insights => (
        Icons.insights_outlined,
        'Verified sales insight services are not connected in this release.',
      ),
      AppDestination.forecast => (
        Icons.auto_graph_outlined,
        'Forecast services are not enabled in this release.',
      ),
      AppDestination.catalogue => (
        Icons.qr_code_2_outlined,
        'The ERP catalogue is not downloaded or displayed in this release.',
      ),
      AppDestination.notifications => (
        Icons.notifications_none_outlined,
        'The shared ERP notification inbox is not connected in this release.',
      ),
      AppDestination.settings => (
        Icons.tune_outlined,
        'Additional preferences are not part of this UI-only slice.',
      ),
      AppDestination.dashboard || AppDestination.profile => (
        Icons.info_outline,
        'This section is not available in the current preview.',
      ),
    };

    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Icon(icon, size: 32, color: Theme.of(context).colorScheme.primary),
        const SizedBox(height: 16),
        Text(
          destination.pageTitle,
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 12),
        ContentStatePanel(
          state: ContentState.preview,
          title: 'Not connected',
          message: '$detail No business-data request was made.',
        ),
      ],
    );
  }
}
