import 'package:flutter/material.dart';

import 'app_colors.dart';

enum ContentState {
  loading,
  empty,
  unavailable,
  stale,
  error,
  unauthorized,
  preview,
}

final class ContentStatePanel extends StatelessWidget {
  const ContentStatePanel({
    required this.state,
    required this.title,
    required this.message,
    super.key,
  });

  final ContentState state;
  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    final (icon, color) = switch (state) {
      ContentState.loading => (Icons.hourglass_top, AppColors.primaryBrown),
      ContentState.empty => (Icons.inbox_outlined, AppColors.mutedTaupe),
      ContentState.unavailable => (
        Icons.cloud_off_outlined,
        AppColors.mutedTaupe,
      ),
      ContentState.stale => (Icons.update_outlined, AppColors.warning),
      ContentState.error => (Icons.error_outline, AppColors.error),
      ContentState.unauthorized => (Icons.lock_outline, AppColors.warning),
      ContentState.preview => (
        Icons.visibility_outlined,
        AppColors.primaryBrown,
      ),
    };

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Semantics(
          liveRegion:
              state == ContentState.loading ||
              state == ContentState.error ||
              state == ContentState.stale,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (state == ContentState.loading)
                const Padding(
                  padding: EdgeInsets.all(2),
                  child: SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                )
              else
                Icon(icon, color: color, semanticLabel: title),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 6),
                    Text(message),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
