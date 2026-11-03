import 'package:flutter/material.dart';

import 'content_state_panel.dart';

final class DashboardSection extends StatelessWidget {
  const DashboardSection({
    required this.title,
    required this.description,
    required this.icon,
    required this.stateTitle,
    required this.stateMessage,
    required this.state,
    super.key,
  });

  final String title;
  final String description;
  final IconData icon;
  final String stateTitle;
  final String stateMessage;
  final ContentState state;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Row(
        children: [
          Icon(icon, color: Theme.of(context).colorScheme.primary),
          const SizedBox(width: 10),
          Expanded(
            child: Text(title, style: Theme.of(context).textTheme.titleMedium),
          ),
        ],
      ),
      Padding(
        padding: const EdgeInsets.only(left: 34, top: 4, bottom: 10),
        child: Text(description),
      ),
      ContentStatePanel(state: state, title: stateTitle, message: stateMessage),
    ],
  );
}

final class DashboardQuickActions extends StatelessWidget {
  const DashboardQuickActions({required this.actions, super.key});

  final List<Widget> actions;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Quick actions', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 12),
          Wrap(spacing: 8, runSpacing: 8, children: actions),
          const SizedBox(height: 8),
          Text(
            'Links open UI previews only; they do not start workflows or load business data.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    ),
  );
}
