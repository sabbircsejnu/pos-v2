import 'package:flutter/material.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/shared/design_system/dashboard_components.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

enum InsightsPeriod {
  sevenDays('7 Days'),
  thirtyDays('30 Days'),
  custom('Custom');

  const InsightsPeriod(this.label);

  final String label;
}

final class SalesInsightsPage extends StatefulWidget {
  const SalesInsightsPage({
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
  State<SalesInsightsPage> createState() => _SalesInsightsPageState();
}

final class _SalesInsightsPageState extends State<SalesInsightsPage> {
  InsightsPeriod _selectedPeriod = InsightsPeriod.sevenDays;

  @override
  Widget build(BuildContext context) {
    if (widget.role == MobileRole.unsupported) {
      return const ContentStatePanel(
        state: ContentState.unauthorized,
        title: 'Insights unavailable',
        message: 'This account role is not mapped to mobile sales insights. Contact your administrator.',
      );
    }

    final sections = _sectionsFor(widget.role);
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      children: [
        Text(
          'Hello, ${widget.profileName}',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 4),
        Text(widget.role.label, style: Theme.of(context).textTheme.bodyMedium),
        const SizedBox(height: 16),
        Text('Insights', style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 4),
        Text(_descriptionFor(widget.role)),
        const SizedBox(height: 12),
        _ScopeCard(
          role: widget.role,
          businessId: widget.businessId,
          outletName: widget.outletName,
        ),
        const SizedBox(height: 12),
        const ContentStatePanel(
          state: ContentState.preview,
          title: 'Not connected',
          message: 'Reporting data is unavailable. No sales, product, discount, margin or demand values are loaded or estimated.',
        ),
        const SizedBox(height: 16),
        Text(
          'Reporting period',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 4),
        Text(
          'Choose a period for this presentation. Reporting data and custom date selection are not connected.',
          style: Theme.of(context).textTheme.bodyMedium,
        ),
        const SizedBox(height: 8),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: SegmentedButton<InsightsPeriod>(
            segments: [
              for (final period in InsightsPeriod.values)
                ButtonSegment<InsightsPeriod>(
                  value: period,
                  label: Text(period.label),
                ),
            ],
            selected: {_selectedPeriod},
            showSelectedIcon: false,
            style: const ButtonStyle(
              minimumSize: WidgetStatePropertyAll(Size(96, 48)),
            ),
            onSelectionChanged: (selection) {
              setState(() => _selectedPeriod = selection.single);
            },
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Selected: ${_selectedPeriod.label}. This changes the preview selection only; no reporting request is made.',
          key: const ValueKey('selected-insights-period'),
          style: Theme.of(context).textTheme.bodySmall,
        ),
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
                      state: section.state,
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
}

String _descriptionFor(MobileRole role) => switch (role) {
  MobileRole.businessOwner =>
    'Business-wide trends and performance, subject to your ERP grants.',
  MobileRole.manager =>
    'Sales and demand insights for your selected authorized outlet only.',
  MobileRole.salesperson => 'Your own historical sales and activity only. Cashier-specific policy and layout remain provisional.',
  MobileRole.unsupported => '',
};

List<_InsightSection> _sectionsFor(MobileRole role) => switch (role) {
  MobileRole.businessOwner => const [
    _InsightSection(
      title: 'Business sales trend',
      description: 'Historical business-wide sales over the selected period.',
      icon: Icons.show_chart,
      state: ContentState.unavailable,
      unavailableMessage: 'Verified reporting data is not connected. No trend line or comparison is shown.',
    ),
    _InsightSection(
      title: 'Outlet performance',
      description: 'Performance for outlets visible to your ERP grants.',
      icon: Icons.storefront_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'Authorized outlet performance is unavailable until ERP reporting is connected.',
    ),
    _InsightSection(
      title: 'Category performance',
      description: 'Sales and demand signals by product category.',
      icon: Icons.category_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'Category values and comparisons are not available; no prototype values are used.',
    ),
    _InsightSection(
      title: 'Top-performing products',
      description:
          'Product performance based on approved ERP metric definitions.',
      icon: Icons.star_border,
      state: ContentState.unavailable,
      unavailableMessage: 'Product rankings are not calculated on-device and are not connected.',
    ),
    _InsightSection(
      title: 'Product and variant demand',
      description: 'Movement and demand by design, category, colour and size; stock-risk signals only when authorized.',
      icon: Icons.shopping_bag_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'No product velocity, variant demand or stock-out risk is estimated in Flutter.',
    ),
    _InsightSection(
      title: 'Discount distribution',
      description: 'Descriptive discount patterns; association is not proof of causality.',
      icon: Icons.percent,
      state: ContentState.unavailable,
      unavailableMessage: 'Discount reporting is unavailable until its ERP definitions are approved.',
    ),
    _InsightSection(
      title: 'Margin-sensitive insights',
      description: 'Profitability information only when explicitly granted.',
      icon: Icons.lock_outline,
      state: ContentState.unavailable,
      unavailableMessage: 'Margin fields are not loaded or derived in Flutter. ERP field grants remain authoritative.',
    ),
  ],
  MobileRole.manager => const [
    _InsightSection(
      title: 'Outlet sales trend',
      description: 'Historical sales for the selected authorized outlet.',
      icon: Icons.show_chart,
      state: ContentState.unavailable,
      unavailableMessage: 'Outlet reporting is not connected. No other-outlet data is requested or shown.',
    ),
    _InsightSection(
      title: 'Product and variant demand',
      description: 'Design, category, colour and size signals within the selected authorized outlet.',
      icon: Icons.category_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'Product velocity and demand signals are unavailable; no stock-out risk or local estimates are calculated.',
    ),
    _InsightSection(
      title: 'Sales performance',
      description:
          'Outlet sales performance using approved reporting definitions.',
      icon: Icons.insights_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'Verified outlet performance is not connected. Restricted financial fields are omitted.',
    ),
    _InsightSection(
      title: 'Discount insights',
      description: 'Descriptive outlet discount patterns, when permitted.',
      icon: Icons.percent,
      state: ContentState.unavailable,
      unavailableMessage: 'Discount reporting is unavailable. Profit and margin information is not shown for this role.',
    ),
  ],
  MobileRole.salesperson => const [
    _InsightSection(
      title: 'My sales history',
      description: 'Your own historical sales for the selected period.',
      icon: Icons.show_chart,
      state: ContentState.unavailable,
      unavailableMessage: 'Personal reporting is not connected. No peer or business-wide totals are shown.',
    ),
    _InsightSection(
      title: 'My product insights',
      description:
          'Products associated with your own authorized sales activity.',
      icon: Icons.shopping_bag_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'Self-only product insights are unavailable; no product activity is inferred.',
    ),
    _InsightSection(
      title: 'My activity',
      description: 'Your own sales activity and historical comparisons.',
      icon: Icons.receipt_long_outlined,
      state: ContentState.unavailable,
      unavailableMessage: 'Personal activity is not connected. Peer rankings, profit and outlet metrics are not shown.',
    ),
  ],
  MobileRole.unsupported => const [],
};

final class _ScopeCard extends StatelessWidget {
  const _ScopeCard({
    required this.role,
    required this.businessId,
    required this.outletName,
  });

  final MobileRole role;
  final int businessId;
  final String? outletName;

  @override
  Widget build(BuildContext context) {
    final (label, detail) = switch (role) {
      MobileRole.businessOwner => (
        'Business context',
        'Business $businessId · reporting remains subject to ERP grants.',
      ),
      MobileRole.manager => (
        'Authorized outlet context',
        outletName ?? 'No authorized outlet is currently selected.',
      ),
      MobileRole.salesperson => (
        'Personal reporting scope',
        'Self-only insights · ${outletName ?? 'authorized outlet not selected'}',
      ),
      MobileRole.unsupported => ('Access context', 'Insights are unavailable.'),
    };

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              Icons.filter_alt_outlined,
              color: Theme.of(context).colorScheme.primary,
              semanticLabel: label,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(label, style: Theme.of(context).textTheme.titleSmall),
                  const SizedBox(height: 4),
                  Text(detail),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

final class _InsightSection {
  const _InsightSection({
    required this.title,
    required this.description,
    required this.icon,
    required this.state,
    required this.unavailableMessage,
  });

  final String title;
  final String description;
  final IconData icon;
  final ContentState state;
  final String unavailableMessage;
}
