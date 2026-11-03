import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/shared/design_system/dashboard_components.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

final class RoleDashboardPage extends StatelessWidget {
  const RoleDashboardPage({
    required this.role,
    required this.profileName,
    required this.businessId,
    required this.outletName,
    super.key,
  });

  final MobileRole role;
  final String profileName;
  final int businessId;
  final String? outletName;

  @override
  Widget build(BuildContext context) {
    if (role == MobileRole.unsupported) {
      return const ContentStatePanel(
        state: ContentState.unauthorized,
        title: 'Dashboard unavailable',
        message: 'This account role is not mapped to a mobile dashboard. Contact your administrator.',
      );
    }

    final (title, roleNote) = switch (role) {
      MobileRole.businessOwner => (
        'Business overview',
        'Business-wide reporting is shown only when authorized ERP services are connected.',
      ),
      MobileRole.manager => (
        'Manager dashboard',
        'This dashboard is scoped to the outlet selected from your authorized ERP context.',
      ),
      MobileRole.salesperson => (
        'My dashboard',
        'Cashier and Salesperson share this provisional frontline layout; confirm cashier-specific policy and design before release.',
      ),
      MobileRole.unsupported => throw StateError('Handled above.'),
    };
    final sections = _sectionsFor(role);

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      children: [
        Text(
          'Hello, $profileName',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 4),
        Text(role.label, style: Theme.of(context).textTheme.bodyMedium),
        const SizedBox(height: 16),
        _ContextCard(
          businessId: businessId,
          outletName: outletName,
          scopeLabel: role == MobileRole.businessOwner
              ? 'Business context'
              : 'Assigned outlet context',
        ),
        const SizedBox(height: 16),
        Text(title, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 4),
        Text(roleNote),
        const SizedBox(height: 12),
        const ContentStatePanel(
          state: ContentState.preview,
          title: 'Preview only',
          message: 'No dashboard APIs are connected. Sales, profit, targets, stock and staff data are not loaded or estimated.',
        ),
        const SizedBox(height: 16),
        DashboardQuickActions(actions: _quickActions(context, role)),
        const SizedBox(height: 20),
        LayoutBuilder(
          builder: (context, constraints) {
            final columns = constraints.maxWidth >= 660 ? 2 : 1;
            final sectionWidth = columns == 1
                ? constraints.maxWidth
                : (constraints.maxWidth - 16) / 2;
            return Wrap(
              spacing: 16,
              runSpacing: 20,
              children: [
                for (final section in sections)
                  SizedBox(
                    width: sectionWidth,
                    child: DashboardSection(
                      title: section.title,
                      description: section.description,
                      icon: section.icon,
                      state: ContentState.unavailable,
                      stateTitle: 'Not connected',
                      stateMessage: section.unavailableMessage,
                    ),
                  ),
              ],
            );
          },
        ),
      ],
    );
  }

  List<Widget> _quickActions(BuildContext context, MobileRole role) {
    final destinations = switch (role) {
      MobileRole.businessOwner => const [
        ('/stock-take', 'Stock Take', Icons.inventory_2_outlined),
        ('/insights', 'Insights', Icons.insights_outlined),
      ],
      MobileRole.manager => const [
        ('/stock-take', 'Stock Take', Icons.inventory_2_outlined),
        ('/insights', 'Insights', Icons.insights_outlined),
        ('/catalogue', 'Catalogue', Icons.qr_code_2_outlined),
      ],
      MobileRole.salesperson => const [
        ('/stock-take', 'Stock Take', Icons.inventory_2_outlined),
        ('/notifications', 'Alerts', Icons.notifications_none_outlined),
      ],
      MobileRole.unsupported => const [],
    };

    return [
      for (final (path, label, icon) in destinations)
        OutlinedButton.icon(
          onPressed: () => context.go(path),
          icon: Icon(icon),
          label: Text(label),
        ),
    ];
  }

  List<_DashboardSectionDefinition> _sectionsFor(
    MobileRole role,
  ) => switch (role) {
    MobileRole.businessOwner => const [
      _DashboardSectionDefinition(
        title: 'Sales summary',
        description: 'Sales, invoices, units, average invoice and returns.',
        icon: Icons.receipt_long_outlined,
        unavailableMessage: 'Approved reporting is not connected. Currency, reporting window and metric definitions will come from ERP data.',
      ),
      _DashboardSectionDefinition(
        title: 'Sales trends',
        description: 'Seven- and thirty-day sales trends and change.',
        icon: Icons.show_chart,
        unavailableMessage:
            'No trend data is available to plot. No sample series is shown.',
      ),
      _DashboardSectionDefinition(
        title: 'Outlet performance',
        description: 'Authorized outlet comparisons and rankings.',
        icon: Icons.storefront_outlined,
        unavailableMessage: 'Outlet comparisons require an approved, permission-scoped reporting response.',
      ),
      _DashboardSectionDefinition(
        title: 'Top-performing products',
        description: 'Product and design performance for the selected period.',
        icon: Icons.star_border,
        unavailableMessage: 'Product rankings are unavailable until reporting data and metric definitions are approved.',
      ),
      _DashboardSectionDefinition(
        title: 'Discount and margin insights',
        description: 'Discounts, returns and gross margin when granted.',
        icon: Icons.percent,
        unavailableMessage: 'Discount insights and restricted margin fields are not connected; ERP grants remain authoritative.',
      ),
      _DashboardSectionDefinition(
        title: 'Stock alerts',
        description: 'Operational alerts for authorized locations.',
        icon: Icons.inventory_outlined,
        unavailableMessage:
            'No inventory or alert data is loaded in this UI-only dashboard.',
      ),
    ],
    MobileRole.manager => const [
      _DashboardSectionDefinition(
        title: 'Assigned-outlet summary',
        description: 'Sales and operational overview for the selected outlet.',
        icon: Icons.store_outlined,
        unavailableMessage: 'Outlet reporting is disconnected. The visible outlet name comes only from the authenticated ERP session.',
      ),
      _DashboardSectionDefinition(
        title: 'Sales versus target',
        description: 'Authorized sales and target-attainment presentation.',
        icon: Icons.track_changes_outlined,
        unavailableMessage: 'Sales and target values require approved ERP definitions; no target or attainment is inferred.',
      ),
      _DashboardSectionDefinition(
        title: 'Staff performance',
        description: 'Outlet staff leaderboard, when permitted.',
        icon: Icons.groups_outlined,
        unavailableMessage: 'Staff rankings are not loaded. Staff visibility and leaderboard access require ERP authorization.',
      ),
      _DashboardSectionDefinition(
        title: 'Sales and product trends',
        description: 'Invoice, discount and product signals for this outlet.',
        icon: Icons.query_stats_outlined,
        unavailableMessage:
            'No outlet trend data is available to plot or compare.',
      ),
      _DashboardSectionDefinition(
        title: 'Stock Take status',
        description: 'Read-only progress summary for authorized sessions.',
        icon: Icons.fact_check_outlined,
        unavailableMessage: 'Stock Take status is not connected; this dashboard does not create or update a count.',
      ),
      _DashboardSectionDefinition(
        title: 'Stock alerts',
        description: 'Outlet-specific operational alerts.',
        icon: Icons.warning_amber_outlined,
        unavailableMessage:
            'No inventory or alert data is loaded in this UI-only dashboard.',
      ),
    ],
    MobileRole.salesperson => const [
      _DashboardSectionDefinition(
        title: 'My sales activity',
        description:
            'Self-only sales and average transaction, subject to policy.',
        icon: Icons.point_of_sale_outlined,
        unavailableMessage: 'Personal activity is not loaded. The approved role policy and reporting grant must define what is visible.',
      ),
      _DashboardSectionDefinition(
        title: 'Target progress',
        description: 'Target, attainment and remaining amount when assigned.',
        icon: Icons.flag_outlined,
        unavailableMessage: 'No target is available. The UI does not treat a missing target as zero.',
      ),
      _DashboardSectionDefinition(
        title: 'Invoices and units',
        description: 'Personal invoice and unit activity.',
        icon: Icons.receipt_outlined,
        unavailableMessage: 'Invoice and unit activity is unavailable until an authorized reporting service is connected.',
      ),
      _DashboardSectionDefinition(
        title: 'Outlet and tasks',
        description: 'Assigned outlet context and relevant work previews.',
        icon: Icons.task_alt_outlined,
        unavailableMessage: 'The selected outlet is shown above. Task and Stock Take records are not loaded.',
      ),
    ],
    MobileRole.unsupported => const [],
  };
}

final class _ContextCard extends StatelessWidget {
  const _ContextCard({
    required this.businessId,
    required this.outletName,
    required this.scopeLabel,
  });

  final int businessId;
  final String? outletName;
  final String scopeLabel;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(scopeLabel, style: Theme.of(context).textTheme.labelLarge),
          const SizedBox(height: 8),
          Text(
            'Business $businessId',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 4),
          Text(outletName ?? 'No authorized outlet is selected'),
        ],
      ),
    ),
  );
}

final class _DashboardSectionDefinition {
  const _DashboardSectionDefinition({
    required this.title,
    required this.description,
    required this.icon,
    required this.unavailableMessage,
  });

  final String title;
  final String description;
  final IconData icon;
  final String unavailableMessage;
}
