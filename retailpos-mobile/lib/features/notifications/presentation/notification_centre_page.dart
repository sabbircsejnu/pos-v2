import 'package:flutter/material.dart';
import 'package:iranimart_business_mobile/features/notifications/domain/notification_presentation_models.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

final class NotificationCentrePage extends StatefulWidget {
  const NotificationCentrePage({
    required this.businessId,
    required this.outletName,
    super.key,
  });

  final int businessId;
  final String? outletName;

  @override
  State<NotificationCentrePage> createState() => _NotificationCentrePageState();
}

final class NotificationBellButton extends StatelessWidget {
  const NotificationBellButton({
    required this.onPressed,
    required this.unreadCount,
    super.key,
  });

  final VoidCallback onPressed;
  final int? unreadCount;

  @override
  Widget build(BuildContext context) {
    final count = unreadCount;
    return Semantics(
      button: true,
      label: switch (count) {
        null => 'Notifications, unread count unavailable',
        0 => 'Notifications, no unread items',
        final count => 'Notifications, $count unread',
      },
      child: IconButton(
        tooltip: switch (count) {
          null => 'Notifications · unread count unavailable',
          0 => 'Notifications · no unread items',
          final count => 'Notifications · $count unread',
        },
        onPressed: onPressed,
        icon: Badge(
          isLabelVisible: count == null || count > 0,
          label: Text(count == null ? '—' : _badgeCount(count)),
          child: const Icon(Icons.notifications_outlined),
        ),
      ),
    );
  }
}

final class _NotificationCentrePageState extends State<NotificationCentrePage> {
  NotificationFilter _filter = const NotificationFilter.all();

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
    children: [
      Row(
        children: [
          Expanded(
            child: Text(
              'Notifications',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
          ),
          const UnreadNotificationBadge(count: null),
        ],
      ),
      const SizedBox(height: 12),
      _NotificationScopeCard(
        businessId: widget.businessId,
        outletName: widget.outletName,
      ),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.preview,
        title: 'Inbox not connected',
        message: 'Notifications and unread count come from the authorized ERP inbox. No notification request was made.',
      ),
      const SizedBox(height: 16),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final filter in _filters)
            FilterChip(
              label: Text(filter.label),
              selected: _filter.label == filter.label,
              onSelected: (_) => setState(() => _filter = filter),
            ),
        ],
      ),
      const SizedBox(height: 12),
      DropdownButtonFormField<NotificationType?>(
        isExpanded: true,
        initialValue: _filter.type,
        decoration: const InputDecoration(
          labelText: 'Filter by notification type',
          border: OutlineInputBorder(),
        ),
        items: [
          const DropdownMenuItem<NotificationType?>(
            value: null,
            child: Text('All notification types'),
          ),
          for (final type in NotificationType.values)
            DropdownMenuItem<NotificationType?>(
              value: type,
              child: Text(
                type.label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
        ],
        onChanged: (type) => setState(
          () => _filter = type == null
              ? const NotificationFilter.all()
              : NotificationFilter.type(type),
        ),
      ),
      const SizedBox(height: 12),
      OutlinedButton.icon(
        onPressed: null,
        icon: const Icon(Icons.done_all),
        label: const Text('Mark all read · Not connected'),
      ),
      const SizedBox(height: 20),
      Text(
        'Notification history',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      Text(
        'Filter: ${_filter.label}. History remains unavailable until the ERP inbox is connected.',
        key: const ValueKey('notification-selected-filter'),
        style: Theme.of(context).textTheme.bodyMedium,
      ),
      const SizedBox(height: 12),
      NotificationHistoryPanel(
        state: NotificationInboxState.unavailable,
        filter: _filter,
      ),
      const SizedBox(height: 20),
      const NotificationPreferencesPanel(),
    ],
  );

  static final _filters = [
    const NotificationFilter.all(),
    const NotificationFilter.unread(),
    for (final category in NotificationCategory.values)
      NotificationFilter.category(category),
  ];
}

final class _NotificationScopeCard extends StatelessWidget {
  const _NotificationScopeCard({
    required this.businessId,
    required this.outletName,
  });

  final int businessId;
  final String? outletName;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Signed-in business',
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 4),
          Text('Business $businessId'),
          if (outletName != null) ...[
            const SizedBox(height: 8),
            Text('Selected authorized outlet'),
            Text(outletName!),
          ],
          const SizedBox(height: 8),
          Text(
            'Notification content and recipient scope are determined by ERP permissions, not this displayed context.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    ),
  );
}

enum NotificationInboxState {
  available,
  loading,
  empty,
  unavailable,
  stale,
  error,
  unauthorized,
}

final class NotificationHistoryPanel extends StatelessWidget {
  const NotificationHistoryPanel({
    required this.state,
    this.notifications = const [],
    this.filter = const NotificationFilter.all(),
    this.onOpen,
    this.onMarkRead,
    super.key,
  });

  final NotificationInboxState state;
  final List<NotificationPresentation> notifications;
  final NotificationFilter filter;
  final ValueChanged<NotificationPresentation>? onOpen;
  final ValueChanged<NotificationPresentation>? onMarkRead;

  @override
  Widget build(BuildContext context) {
    switch (state) {
      case NotificationInboxState.available:
        final filtered = notifications
            .where(filter.includes)
            .toList(growable: false);
        if (filtered.isEmpty) {
          return const ContentStatePanel(
            state: ContentState.empty,
            title: 'No notifications match',
            message: 'There are no notifications for this filter.',
          );
        }
        return Column(
          children: [
            for (final notification in filtered)
              NotificationListTile(
                notification: notification,
                onOpen: onOpen == null ? null : () => onOpen!(notification),
                onMarkRead: onMarkRead == null
                    ? null
                    : () => onMarkRead!(notification),
              ),
          ],
        );
      case NotificationInboxState.empty:
        return const ContentStatePanel(
          state: ContentState.empty,
          title: 'No notifications',
          message: 'There are no notifications in this inbox.',
        );
      case NotificationInboxState.unavailable:
        return const ContentStatePanel(
          state: ContentState.unavailable,
          title: 'Notification history unavailable',
          message: 'This device does not have an authorized inbox copy.',
        );
      case NotificationInboxState.loading:
        return const ContentStatePanel(
          state: ContentState.loading,
          title: 'Loading notifications',
          message: 'Checking the authorized ERP inbox.',
        );
      case NotificationInboxState.stale:
        return const ContentStatePanel(
          state: ContentState.stale,
          title: 'Inbox may be out of date',
          message: 'Read state and notification history need ERP verification.',
        );
      case NotificationInboxState.unauthorized:
        return const ContentStatePanel(
          state: ContentState.unauthorized,
          title: 'Inbox access not authorized',
          message: 'The ERP did not authorize this notification scope.',
        );
      case NotificationInboxState.error:
        return const ContentStatePanel(
          state: ContentState.error,
          title: 'Inbox could not be loaded',
          message: 'No notification result is available. No change was made.',
        );
    }
  }
}

final class NotificationListTile extends StatelessWidget {
  const NotificationListTile({
    required this.notification,
    this.onOpen,
    this.onMarkRead,
    this.showActions = true,
    super.key,
  });

  final NotificationPresentation notification;
  final VoidCallback? onOpen;
  final VoidCallback? onMarkRead;
  final bool showActions;

  @override
  Widget build(BuildContext context) {
    final severityColor = _severityColor(context, notification.severity);
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: InkWell(
        onTap: onOpen,
        borderRadius: BorderRadius.circular(24),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Wrap(
                spacing: 8,
                runSpacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  if (!notification.isRead)
                    Semantics(
                      label: 'Unread',
                      child: Icon(Icons.circle, size: 12),
                    ),
                  Chip(label: Text(notification.isRead ? 'Read' : 'Unread')),
                  _SeverityLabel(
                    severity: notification.severity,
                    color: severityColor,
                  ),
                  _CategoryLabel(category: notification.type.category),
                ],
              ),
              const SizedBox(height: 10),
              Text(
                notification.title,
                style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  fontWeight: notification.isRead
                      ? FontWeight.normal
                      : FontWeight.bold,
                ),
              ),
              const SizedBox(height: 4),
              Text(notification.summary),
              const SizedBox(height: 8),
              Text(
                _formatEventTime(notification.eventTimeUtc),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 8),
              if (showActions)
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    OutlinedButton(
                      onPressed: onOpen,
                      child: const Text('View details'),
                    ),
                    if (!notification.isRead)
                      TextButton.icon(
                        onPressed: onMarkRead,
                        icon: const Icon(Icons.done),
                        label: Text(
                          onMarkRead == null
                              ? 'Mark read · Not connected'
                              : 'Mark as read · Preview only',
                        ),
                      ),
                  ],
                ),
              if (showActions && !notification.isRead && onMarkRead != null)
                Text(
                  'Preview action only; read state is not synchronized.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
            ],
          ),
        ),
      ),
    );
  }
}

final class UnreadNotificationBadge extends StatelessWidget {
  const UnreadNotificationBadge({required this.count, super.key});

  final int? count;

  @override
  Widget build(BuildContext context) {
    final value = count;
    if (value == null) {
      return Semantics(
        label: 'Unread notification count unavailable',
        child: Chip(label: Text('Unread —')),
      );
    }
    if (value <= 0) {
      return Semantics(
        label: 'No unread notifications',
        child: Chip(label: Text('Unread 0')),
      );
    }
    final display = value > 99 ? '99+' : '$value';
    return Semantics(
      label: '$value unread notifications',
      child: Badge(
        label: Text(display),
        child: const Padding(
          padding: EdgeInsets.all(8),
          child: Icon(Icons.notifications_outlined),
        ),
      ),
    );
  }
}

final class NotificationPreferencesPanel extends StatelessWidget {
  const NotificationPreferencesPanel({super.key});

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Notification preferences',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 4),
          Text(
            'Optional preferences require an approved ERP policy. Mandatory security and critical alerts cannot be disabled here.',
          ),
          const SizedBox(height: 8),
          const SwitchListTile(
            contentPadding: EdgeInsets.zero,
            title: Text('Optional business notifications'),
            subtitle: Text('Preferences are not connected or saved.'),
            value: false,
            onChanged: null,
          ),
        ],
      ),
    ),
  );
}

final class NotificationDetailPanel extends StatelessWidget {
  const NotificationDetailPanel({
    required this.notification,
    this.onMarkRead,
    this.destinationState = NotificationDestinationState.unavailable,
    this.onOpenDestination,
    super.key,
  });

  final NotificationPresentation notification;
  final VoidCallback? onMarkRead;
  final NotificationDestinationState destinationState;
  final VoidCallback? onOpenDestination;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      Text(
        'Notification details',
        style: Theme.of(context).textTheme.headlineSmall,
      ),
      const SizedBox(height: 12),
      NotificationListTile(notification: notification, showActions: false),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _DetailRow(label: 'Type', value: notification.type.label),
              _DetailRow(
                label: 'Category',
                value: notification.type.category.label,
              ),
              _DetailRow(label: 'Severity', value: notification.severity.label),
              _DetailRow(
                label: 'Event time (UTC)',
                value: notification.eventTimeUtc.toUtc().toIso8601String(),
              ),
              _DetailRow(
                label: 'Read state',
                value: notification.isRead ? 'Read' : 'Unread',
              ),
            ],
          ),
        ),
      ),
      const SizedBox(height: 12),
      if (notification.routeKey != null)
        NotificationDestinationPanel(
          routeKey: notification.routeKey!,
          state: destinationState,
          onOpen: onOpenDestination,
        ),
      if (!notification.isRead) ...[
        const SizedBox(height: 12),
        OutlinedButton.icon(
          onPressed: onMarkRead,
          icon: const Icon(Icons.done),
          label: Text(
            onMarkRead == null
                ? 'Mark as read · Not connected'
                : 'Mark as read · Preview only',
          ),
        ),
        if (onMarkRead != null)
          Text(
            'Preview action only; read state is not synchronized with the ERP.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
      ],
    ],
  );
}

final class NotificationDestinationPanel extends StatelessWidget {
  const NotificationDestinationPanel({
    required this.routeKey,
    required this.state,
    this.onOpen,
    super.key,
  });

  final NotificationRouteKey routeKey;
  final NotificationDestinationState state;
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) {
    final (panelState, message) = switch (state) {
      NotificationDestinationState.authorized => (
        ContentState.preview,
        'Opening this destination still requires current ERP authorization.',
      ),
      NotificationDestinationState.unavailable => (
        ContentState.unavailable,
        'The destination is unavailable; no external link was opened.',
      ),
      NotificationDestinationState.expired => (
        ContentState.stale,
        'This notification destination has expired.',
      ),
      NotificationDestinationState.inaccessible => (
        ContentState.unauthorized,
        'Access is unavailable for the current role or outlet context.',
      ),
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ContentStatePanel(
          state: panelState,
          title: state.label,
          message: message,
        ),
        if (state == NotificationDestinationState.authorized)
          OutlinedButton.icon(
            onPressed: onOpen,
            icon: const Icon(Icons.open_in_new),
            label: Text(
              onOpen == null
                  ? '${routeKey.label} · Not connected'
                  : 'Open ${routeKey.label} · Reauthorize',
            ),
          ),
      ],
    );
  }
}

final class _SeverityLabel extends StatelessWidget {
  const _SeverityLabel({required this.severity, required this.color});

  final NotificationSeverity severity;
  final Color color;

  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Severity: ${severity.label}',
    child: Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color),
      ),
      child: Text(
        severity.label,
        style: Theme.of(context).textTheme.labelMedium?.copyWith(color: color),
      ),
    ),
  );
}

final class _CategoryLabel extends StatelessWidget {
  const _CategoryLabel({required this.category});

  final NotificationCategory category;

  @override
  Widget build(BuildContext context) => Chip(label: Text(category.label));
}

final class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 8),
    child: Wrap(
      spacing: 8,
      runSpacing: 4,
      children: [
        Text('$label:', style: Theme.of(context).textTheme.labelLarge),
        SelectableText(value),
      ],
    ),
  );
}

String _formatEventTime(DateTime eventTimeUtc) {
  final utc = eventTimeUtc.toUtc();
  String twoDigits(int value) => value.toString().padLeft(2, '0');
  return '${utc.year}-${twoDigits(utc.month)}-${twoDigits(utc.day)} '
      '${twoDigits(utc.hour)}:${twoDigits(utc.minute)} UTC';
}

Color _severityColor(BuildContext context, NotificationSeverity severity) {
  final colors = Theme.of(context).colorScheme;
  return switch (severity) {
    NotificationSeverity.info || NotificationSeverity.low => colors.primary,
    NotificationSeverity.medium => colors.tertiary,
    NotificationSeverity.high || NotificationSeverity.critical => colors.error,
  };
}

String _badgeCount(int count) => count > 99 ? '99+' : '$count';
