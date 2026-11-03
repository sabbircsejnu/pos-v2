import 'package:flutter/material.dart';

import '../../../core/domain/mobile_role.dart';
import '../../../shared/design_system/content_state_panel.dart';
import '../domain/stock_take_presentation_models.dart';

final class StockTakePage extends StatefulWidget {
  const StockTakePage({
    required this.role,
    required this.businessId,
    required this.outletName,
    required this.hasInvalidSelectedOutlet,
    required this.selectedSection,
    required this.onSectionSelected,
    super.key,
  });

  final MobileRole role;
  final int? businessId;
  final String? outletName;
  final bool hasInvalidSelectedOutlet;
  final StockTakeSection selectedSection;
  final ValueChanged<StockTakeSection> onSectionSelected;

  @override
  State<StockTakePage> createState() => _StockTakePageState();
}

final class _StockTakePageState extends State<StockTakePage> {
  StockTakeScope? _scope;
  StockTakeHistoryFilter _historyFilter = StockTakeHistoryFilter.all;

  @override
  Widget build(BuildContext context) {
    if (widget.role == MobileRole.unsupported) {
      return const Padding(
        padding: EdgeInsets.all(16),
        child: ContentStatePanel(
          state: ContentState.unauthorized,
          title: 'Stock Take unavailable',
          message: 'This account does not have a supported mobile role. No stock session data was requested.',
        ),
      );
    }

    return SingleChildScrollView(
      key: const ValueKey('stock-take-page'),
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Stock Take', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 12),
          _StockTakeScopeCard(
            businessId: widget.businessId,
            outletName: widget.outletName,
            hasInvalidSelectedOutlet: widget.hasInvalidSelectedOutlet,
          ),
          const SizedBox(height: 12),
          DropdownButtonFormField<StockTakeSection>(
            key: const ValueKey('stock-take-section-selector'),
            initialValue: widget.selectedSection,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: 'Stock Take section',
              border: OutlineInputBorder(),
            ),
            items: [
              for (final section in StockTakeSection.values)
                DropdownMenuItem(value: section, child: Text(section.label)),
            ],
            onChanged: (section) {
              if (section != null) widget.onSectionSelected(section);
            },
          ),
          const SizedBox(height: 16),
          switch (widget.selectedSection) {
            StockTakeSection.overview => _overview(context),
            StockTakeSection.create => _create(context),
            StockTakeSection.count => _count(context),
            StockTakeSection.synchronization => _synchronization(context),
            StockTakeSection.review => _review(context),
            StockTakeSection.reconciliation => _reconciliation(context),
            StockTakeSection.history => _history(context),
          },
          const SizedBox(height: 16),
          const ContentStatePanel(
            state: ContentState.preview,
            title: 'Presentation-only workflow',
            message: 'No stock sessions, catalogue, count, synchronization, submission, approval, inventory adjustment, or audit records were requested or changed.',
          ),
        ],
      ),
    );
  }

  Widget _overview(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Sessions', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      const StockTakeCollectionStatePanel(
        state: StockTakeCollectionState.unavailable,
        title: 'Active session unavailable',
        message: 'No active or available session is shown.',
      ),
      const SizedBox(height: 8),
      const StockTakeCollectionStatePanel(
        state: StockTakeCollectionState.unavailable,
        title: 'Pending sessions unavailable',
        message: 'Pending count and review sessions are not loaded; no session records were fabricated.',
      ),
      const SizedBox(height: 8),
      const StockTakeCollectionStatePanel(
        state: StockTakeCollectionState.unavailable,
        title: 'History unavailable',
        message: 'Historical Stock Take records are not connected.',
      ),
      const SizedBox(height: 12),
      Text(
        'Session status guide · not live session data',
        style: Theme.of(context).textTheme.titleMedium,
      ),
      const SizedBox(height: 8),
      Wrap(
        spacing: 8,
        runSpacing: 4,
        children: [
          for (final status in StockTakeSessionStatus.values)
            StockTakeStatusChip(status: status),
        ],
      ),
      const SizedBox(height: 12),
      _WorkflowActions(
        onSelect: widget.onSectionSelected,
        sections: const [
          StockTakeSection.create,
          StockTakeSection.synchronization,
          StockTakeSection.history,
        ],
      ),
    ],
  );

  Widget _create(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Create Stock Take', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      const Text(
        'The authenticated Business and authorized Outlet context above is '
        'display-only. Server authorization is required before a session can start.',
      ),
      const SizedBox(height: 12),
      DropdownButtonFormField<StockTakeScope>(
        key: const ValueKey('stock-take-scope-selector'),
        initialValue: _scope,
        isExpanded: true,
        decoration: const InputDecoration(
          labelText: 'Counting scope',
          border: OutlineInputBorder(),
        ),
        items: [
          for (final scope in StockTakeScope.values)
            DropdownMenuItem(value: scope, child: Text(scope.label)),
        ],
        onChanged: (scope) => setState(() => _scope = scope),
      ),
      if (_scope != null) ...[
        const SizedBox(height: 12),
        ContentStatePanel(
          state: ContentState.unavailable,
          title: switch (_scope!) {
            StockTakeScope.fullOutlet => 'Outlet scope unavailable',
            StockTakeScope.category => 'Category selection unavailable',
            StockTakeScope.selectedVariants => 'Variant selection unavailable',
          },
          message: switch (_scope!) {
            StockTakeScope.fullOutlet => 'Authorized location details and server-supported full-outlet scope are not available.',
            StockTakeScope.category =>
              'Category options must come from the authorized ERP catalogue.',
            StockTakeScope.selectedVariants => 'Select exact ERP variants from the authorized catalogue; product families alone are not countable.',
          },
        ),
      ],
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Catalogue and session creation unavailable',
        message: 'A current authorized catalogue and ERP-created session are required. Choosing a display scope does not create a session.',
      ),
      const SizedBox(height: 12),
      const FilledButton(
        onPressed: null,
        child: Text('Start session unavailable'),
      ),
    ],
  );

  Widget _count(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Scan and count', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Counting session unavailable',
        message: 'A session and pinned verified catalogue are required before scanning or manual lookup.',
      ),
      const SizedBox(height: 12),
      TextField(
        key: const ValueKey('stock-take-raw-barcode'),
        enabled: true,
        keyboardType: TextInputType.text,
        decoration: const InputDecoration(
          labelText: 'Manual barcode or product search',
          hintText: 'Enter the exact barcode as text',
          helperText: 'Raw barcode strings are not parsed as numbers; leading zeros are preserved.',
          border: OutlineInputBorder(),
          prefixIcon: Icon(Icons.search),
        ),
      ),
      const SizedBox(height: 8),
      const OutlinedButton(onPressed: null, child: Text('Lookup unavailable')),
      const SizedBox(height: 12),
      const StockTakeVariantIdentityPanel(),
      const SizedBox(height: 12),
      const StockTakeCountControls(),
      const SizedBox(height: 12),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          OutlinedButton.icon(
            onPressed: null,
            icon: Icon(Icons.undo),
            label: Text('Undo unavailable'),
          ),
          OutlinedButton.icon(
            onPressed: null,
            icon: Icon(Icons.pause_circle_outline),
            label: Text('Pause unavailable'),
          ),
          OutlinedButton.icon(
            onPressed: null,
            icon: Icon(Icons.play_circle_outline),
            label: Text('Resume unavailable'),
          ),
        ],
      ),
      const SizedBox(height: 12),
      Text(
        'Barcode exceptions',
        style: Theme.of(context).textTheme.titleMedium,
      ),
      const SizedBox(height: 8),
      for (final issue in StockTakeBarcodeException.values) ...[
        StockTakeBarcodeExceptionPanel(issue: issue),
        const SizedBox(height: 8),
      ],
      const OutlinedButton(
        onPressed: null,
        child: Text('Camera scanning is not available'),
      ),
    ],
  );

  Widget _synchronization(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(
        'Offline and synchronization',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Offline readiness unknown',
        message: 'No verified local catalogue, active offline session, pending operation queue, or server acknowledgement is connected.',
      ),
      const SizedBox(height: 12),
      for (final status in [
        StockTakeSyncStatus.offlineReady,
        StockTakeSyncStatus.staleCatalogue,
        StockTakeSyncStatus.pending,
        StockTakeSyncStatus.syncing,
        StockTakeSyncStatus.failed,
        StockTakeSyncStatus.conflicted,
        StockTakeSyncStatus.serverAccepted,
        StockTakeSyncStatus.sessionExpired,
      ]) ...[
        StockTakeSyncStatusPanel(status: status, previewOnly: true),
        const SizedBox(height: 8),
      ],
      const FilledButton(
        onPressed: null,
        child: Text('Synchronize unavailable'),
      ),
    ],
  );

  Widget _review(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Count review', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Count lines unavailable',
        message: 'No count or exception records are loaded. Expected system quantities remain hidden in the blind-count experience.',
      ),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Counted items and proposed quantities unavailable',
        message: 'Review rows are not loaded. A physical count belongs to an exact ProductVariantId; absent variants are not assumed to have quantity zero.',
      ),
      const SizedBox(height: 12),
      const StockTakeVariantIdentityPanel(),
      const SizedBox(height: 12),
      const StockTakeCountControls(),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Correction unavailable',
        message: 'Count corrections require an identified variant, an active session, and an auditable correction reason.',
      ),
      const TextField(
        enabled: false,
        decoration: InputDecoration(
          labelText: 'Correction reason',
          border: OutlineInputBorder(),
        ),
      ),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Exception resolution unavailable',
        message: 'Unknown, ambiguous, duplicate, retired, and out-of-scope barcodes must be resolved by an authorized ERP process; this screen never guesses a variant.',
      ),
      const SizedBox(height: 12),
      const CheckboxListTile(
        value: false,
        onChanged: null,
        contentPadding: EdgeInsets.zero,
        title: Text('Declare scoped count complete'),
        subtitle: Text(
          'Unavailable until server scope and completeness policy are verified.',
        ),
      ),
    ],
  );

  Widget _reconciliation(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(
        'Submission and reconciliation',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Submission unavailable',
        message: 'Submission requires an authenticated online session, complete acknowledged operations, and an expected ERP revision. No submission was attempted.',
      ),
      const SizedBox(height: 12),
      const StockTakeReconciliationPanel(),
      const SizedBox(height: 12),
      for (final status in StockTakeSubmissionStatus.values.where(
        (status) => status != StockTakeSubmissionStatus.unavailable,
      )) ...[
        StockTakeSubmissionStatusPanel(status: status, previewOnly: true),
        const SizedBox(height: 8),
      ],
      const FilledButton(onPressed: null, child: Text('Submit unavailable')),
      const SizedBox(height: 8),
      const OutlinedButton(onPressed: null, child: Text('Export unavailable')),
    ],
  );

  Widget _history(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Session history', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      DropdownButtonFormField<StockTakeHistoryFilter>(
        key: const ValueKey('stock-take-history-filter'),
        initialValue: _historyFilter,
        isExpanded: true,
        decoration: const InputDecoration(
          labelText: 'Filter history',
          border: OutlineInputBorder(),
        ),
        items: [
          for (final filter in StockTakeHistoryFilter.values)
            DropdownMenuItem(value: filter, child: Text(filter.label)),
        ],
        onChanged: (filter) {
          if (filter != null) setState(() => _historyFilter = filter);
        },
      ),
      const SizedBox(height: 12),
      const TextField(
        decoration: InputDecoration(
          labelText: 'Search session history',
          border: OutlineInputBorder(),
          prefixIcon: Icon(Icons.search),
        ),
      ),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'History not connected',
        message: 'No session, actor, action, timestamp, audit timeline, or export data was requested.',
      ),
      const SizedBox(height: 12),
      const StockTakeSessionTimeline(entries: []),
    ],
  );
}

final class _StockTakeScopeCard extends StatelessWidget {
  const _StockTakeScopeCard({
    required this.businessId,
    required this.outletName,
    required this.hasInvalidSelectedOutlet,
  });

  final int? businessId;
  final String? outletName;
  final bool hasInvalidSelectedOutlet;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Authenticated scope',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          Text(
            businessId == null
                ? 'Business context unavailable'
                : 'Business $businessId',
          ),
          const SizedBox(height: 4),
          Text(
            hasInvalidSelectedOutlet
                ? 'Selected outlet unavailable'
                : outletName == null
                ? 'No selected authorized outlet'
                : 'Authorized outlet context: $outletName',
          ),
          const SizedBox(height: 4),
          const Text(
            'Location authorization is not available in this preview.',
          ),
        ],
      ),
    ),
  );
}

final class _WorkflowActions extends StatelessWidget {
  const _WorkflowActions({required this.onSelect, required this.sections});

  final ValueChanged<StockTakeSection> onSelect;
  final List<StockTakeSection> sections;

  @override
  Widget build(BuildContext context) => Wrap(
    spacing: 8,
    runSpacing: 8,
    children: [
      for (final section in sections)
        OutlinedButton(
          onPressed: () => onSelect(section),
          child: Text(section.label),
        ),
    ],
  );
}

final class StockTakeStatusChip extends StatelessWidget {
  const StockTakeStatusChip({required this.status, super.key});

  final StockTakeSessionStatus status;

  @override
  Widget build(BuildContext context) => Chip(
    avatar: const Icon(Icons.circle, size: 12),
    label: Text(status.label),
  );
}

final class StockTakeCollectionStatePanel extends StatelessWidget {
  const StockTakeCollectionStatePanel({
    required this.state,
    required this.title,
    required this.message,
    super.key,
  });

  final StockTakeCollectionState state;
  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    final contentState = switch (state) {
      StockTakeCollectionState.loading => ContentState.loading,
      StockTakeCollectionState.empty => ContentState.empty,
      StockTakeCollectionState.unavailable => ContentState.unavailable,
      StockTakeCollectionState.stale => ContentState.stale,
      StockTakeCollectionState.error => ContentState.error,
      StockTakeCollectionState.unauthorized => ContentState.unauthorized,
    };
    return ContentStatePanel(
      state: contentState,
      title: title,
      message: message,
    );
  }
}

final class StockTakeSessionSummaryCard extends StatelessWidget {
  const StockTakeSessionSummaryCard({required this.session, super.key});

  final StockTakeSessionSummary session;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            session.reference,
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 4),
          Text('Outlet: ${session.outletName}'),
          Text('Scope: ${session.scopeLabel}'),
          Text('Updated: ${session.updatedAt.toUtc().toIso8601String()}'),
          const SizedBox(height: 8),
          StockTakeStatusChip(status: session.status),
        ],
      ),
    ),
  );
}

final class StockTakeCountReviewItemCard extends StatelessWidget {
  const StockTakeCountReviewItemCard({required this.line, super.key});

  final StockTakeCountReviewLine line;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          StockTakeVariantIdentityPanel(identity: line.identity),
          const SizedBox(height: 8),
          Text(
            'Proposed physical quantity: ${line.physicalQuantity}',
            style: Theme.of(context).textTheme.titleSmall,
          ),
        ],
      ),
    ),
  );
}

final class StockTakeSyncStatusPanel extends StatelessWidget {
  const StockTakeSyncStatusPanel({
    required this.status,
    this.previewOnly = false,
    super.key,
  });

  final StockTakeSyncStatus status;
  final bool previewOnly;

  @override
  Widget build(BuildContext context) {
    final (state, message) = switch (status) {
      StockTakeSyncStatus.unavailable => (
        ContentState.unavailable,
        'No synchronization information is available.',
      ),
      StockTakeSyncStatus.offlineReady => (
        ContentState.preview,
        'This state is valid only after a complete authorized catalogue is durably verified.',
      ),
      StockTakeSyncStatus.staleCatalogue => (
        ContentState.stale,
        'Refresh the catalogue before starting a new session; an active session remains pinned to its original revision.',
      ),
      StockTakeSyncStatus.pending => (
        ContentState.preview,
        'Local operations have not yet received authoritative server acknowledgements.',
      ),
      StockTakeSyncStatus.syncing => (
        ContentState.loading,
        'Synchronization is in progress; this preview performs no work.',
      ),
      StockTakeSyncStatus.failed => (
        ContentState.error,
        'Retryable and terminal failures require different recovery actions.',
      ),
      StockTakeSyncStatus.conflicted => (
        ContentState.error,
        'Conflicts require explicit review; operations are never silently merged or overwritten.',
      ),
      StockTakeSyncStatus.serverAccepted => (
        ContentState.preview,
        'Server acknowledgement is distinct from approval and inventory posting.',
      ),
      StockTakeSyncStatus.sessionExpired => (
        ContentState.unauthorized,
        'Reauthorize with the ERP before submitting; preserve unresolved local work under an approved recovery process.',
      ),
    };
    return ContentStatePanel(
      state: state,
      title: previewOnly ? '${status.label} · state definition' : status.label,
      message: previewOnly
          ? '$message This is a state definition, not a current session status.'
          : message,
    );
  }
}

final class StockTakeVariantIdentityPanel extends StatelessWidget {
  const StockTakeVariantIdentityPanel({this.identity, super.key});

  final StockTakeVariantIdentity? identity;

  @override
  Widget build(BuildContext context) {
    final item = identity;
    if (item == null) {
      return const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Variant identity unavailable',
        message: 'A resolved ERP ProductId and ProductVariantId are required. Main Product Code identifies a product family; counts belong to an exact variant.',
      );
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Resolved variant',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            _IdentityField(label: 'Product', value: item.productName),
            _IdentityField(
              label: 'ProductId',
              value: item.productId.toString(),
            ),
            _IdentityField(
              label: 'ProductVariantId',
              value: item.productVariantId.toString(),
            ),
            _IdentityField(
              label: 'Main Product Code',
              value: item.mainProductCode,
            ),
            _IdentityField(label: 'Variant barcode', value: item.barcode),
            for (final attribute in item.attributes)
              _IdentityField(label: attribute.name, value: attribute.value),
          ],
        ),
      ),
    );
  }
}

final class _IdentityField extends StatelessWidget {
  const _IdentityField({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 3),
    child: Text('$label: $value'),
  );
}

final class StockTakeCountControls extends StatelessWidget {
  const StockTakeCountControls({
    this.quantity,
    this.enabled = false,
    this.onChanged,
    super.key,
  });

  final int? quantity;
  final bool enabled;
  final ValueChanged<int>? onChanged;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Physical quantity',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8,
            children: [
              IconButton(
                tooltip: 'Decrease physical count',
                onPressed: enabled && quantity != null && quantity! > 0
                    ? () => onChanged?.call(quantity! - 1)
                    : null,
                icon: const Icon(Icons.remove_circle_outline),
              ),
              SizedBox(
                width: 96,
                child: TextFormField(
                  key: const ValueKey('stock-take-quantity'),
                  enabled: enabled,
                  initialValue: quantity?.toString() ?? '',
                  keyboardType: TextInputType.number,
                  textAlign: TextAlign.center,
                  decoration: const InputDecoration(
                    labelText: 'Count',
                    border: OutlineInputBorder(),
                  ),
                  onChanged: enabled
                      ? (value) {
                          final parsed = int.tryParse(value);
                          if (parsed != null && parsed >= 0) {
                            onChanged?.call(parsed);
                          }
                        }
                      : null,
                ),
              ),
              IconButton(
                tooltip: 'Increase physical count',
                onPressed: enabled && quantity != null
                    ? () => onChanged?.call(quantity! + 1)
                    : null,
                icon: const Icon(Icons.add_circle_outline),
              ),
            ],
          ),
          const SizedBox(height: 4),
          const Text(
            'Expected ERP quantity is hidden during blind counting. These controls remain disabled without an authorized active session and resolved variant.',
          ),
        ],
      ),
    ),
  );
}

final class StockTakeBarcodeExceptionPanel extends StatelessWidget {
  const StockTakeBarcodeExceptionPanel({
    required this.issue,
    this.rawBarcode,
    super.key,
  });

  final StockTakeBarcodeException issue;
  final String? rawBarcode;

  @override
  Widget build(BuildContext context) => ContentStatePanel(
    state: ContentState.unavailable,
    title: issue.label,
    message: rawBarcode == null
        ? 'No barcode exception records are loaded.'
        : 'Raw barcode "$rawBarcode" requires authorized resolution. It was not mapped to a variant.',
  );
}

final class StockTakeReconciliationPanel extends StatelessWidget {
  const StockTakeReconciliationPanel({
    this.status = StockTakeSubmissionStatus.unavailable,
    this.values = const StockTakeReconciliationValues(),
    super.key,
  });

  final StockTakeSubmissionStatus status;
  final StockTakeReconciliationValues values;

  @override
  Widget build(BuildContext context) {
    String value(int? quantity) => quantity?.toString() ?? 'Unavailable';
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'ERP reconciliation',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            StockTakeSubmissionStatusPanel(status: status),
            const SizedBox(height: 8),
            _IdentityField(
              label: 'System quantity',
              value: value(values.systemQuantity),
            ),
            _IdentityField(
              label: 'Counted quantity',
              value: value(values.countedQuantity),
            ),
            _IdentityField(label: 'Variance', value: value(values.variance)),
            const SizedBox(height: 4),
            const Text(
              'Only ERP-calculated comparisons are authoritative. Posted inventory changes are not performed in mobile.',
            ),
          ],
        ),
      ),
    );
  }
}

final class StockTakeSubmissionStatusPanel extends StatelessWidget {
  const StockTakeSubmissionStatusPanel({
    required this.status,
    this.previewOnly = false,
    super.key,
  });

  final StockTakeSubmissionStatus status;
  final bool previewOnly;

  @override
  Widget build(BuildContext context) {
    final state = switch (status) {
      StockTakeSubmissionStatus.unavailable => ContentState.unavailable,
      StockTakeSubmissionStatus.localPending => ContentState.preview,
      StockTakeSubmissionStatus.serverAccepted => ContentState.preview,
      StockTakeSubmissionStatus.underReview => ContentState.preview,
      StockTakeSubmissionStatus.approved => ContentState.preview,
      StockTakeSubmissionStatus.rejected => ContentState.error,
      StockTakeSubmissionStatus.posted => ContentState.preview,
    };
    final message = switch (status) {
      StockTakeSubmissionStatus.unavailable =>
        'No submission or reconciliation status is available.',
      StockTakeSubmissionStatus.localPending =>
        'Local work has not been accepted by the ERP.',
      StockTakeSubmissionStatus.serverAccepted => 'The ERP accepted the submission; this does not mean it is approved or posted.',
      StockTakeSubmissionStatus.underReview =>
        'ERP review is in progress; mobile cannot approve or adjust inventory.',
      StockTakeSubmissionStatus.approved =>
        'ERP approval is distinct from inventory posting.',
      StockTakeSubmissionStatus.rejected => 'ERP rejected the submitted session; follow the authorized review/recount process.',
      StockTakeSubmissionStatus.posted => 'The ERP reports posting; no accounting or inventory action was issued by mobile.',
    };
    return ContentStatePanel(
      state: state,
      title: previewOnly ? '${status.label} · state definition' : status.label,
      message: previewOnly
          ? '$message This is a workflow definition, not a current session status.'
          : message,
    );
  }
}

final class StockTakeSessionTimeline extends StatelessWidget {
  const StockTakeSessionTimeline({required this.entries, super.key});

  final List<StockTakeTimelineEntry> entries;

  @override
  Widget build(BuildContext context) {
    if (entries.isEmpty) {
      return const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Timeline unavailable',
        message: 'No actor, action, event time, or audit record is available.',
      );
    }
    return Card(
      child: Column(
        children: [
          for (final entry in entries)
            ListTile(
              leading: const Icon(Icons.history),
              title: Text(entry.action),
              subtitle: Text(
                '${entry.actor} · ${entry.occurredAt.toUtc().toIso8601String()}',
              ),
            ),
        ],
      ),
    );
  }
}
