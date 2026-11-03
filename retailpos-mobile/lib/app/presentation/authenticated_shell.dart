import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:iranimart_business_mobile/app/navigation/role_navigation.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/presentation/session_loading_page.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/notifications/presentation/notification_centre_page.dart';

final class AuthenticatedShell extends ConsumerWidget {
  const AuthenticatedShell({
    required this.currentPath,
    required this.child,
    super.key,
  });

  final String currentPath;
  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();

    final profile = session.profile;
    final role = MobileRole.fromServerProfile(
      roleName: profile.roleName,
      isBusinessOwner: profile.isBusinessOwner,
    );
    final bottomItems = RoleNavigation.bottomNavigationFor(role);
    final currentIndex = bottomItems.indexWhere(
      (destination) => destination.path == currentPath,
    );
    final selectedIndex = currentIndex < 0 ? 0 : currentIndex;
    final moreItems = RoleNavigation.moreDestinationsFor(role);
    final width = MediaQuery.sizeOf(context).width;
    final isWide = width >= 700;

    return Scaffold(
      appBar: AppBar(
        title: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Image.asset(
              'assets/brand/iranimart-icon.png',
              width: 32,
              height: 32,
              semanticLabel: 'IraniMart monogram',
            ),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                'IraniMart Business',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
          ],
        ),
        actions: [
          if (RoleNavigation.canAccessPath(role, '/notifications'))
            NotificationBellButton(
              unreadCount: null,
              onPressed: () => context.go('/notifications'),
            ),
          if (moreItems.isNotEmpty)
            PopupMenuButton<AppDestination>(
              tooltip: 'More sections',
              icon: const Icon(Icons.grid_view_outlined),
              onSelected: (destination) => context.go(destination.path),
              itemBuilder: (context) => [
                for (final destination in moreItems)
                  PopupMenuItem(
                    value: destination,
                    child: Text(destination.menuLabel),
                  ),
              ],
            ),
          IconButton(
            tooltip: 'Sign out',
            onPressed: () =>
                ref.read(authSessionControllerProvider.notifier).signOut(),
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: isWide
          ? Row(
              children: [
                NavigationRail(
                  selectedIndex: selectedIndex,
                  onDestinationSelected: (index) =>
                      context.go(bottomItems[index].path),
                  destinations: [
                    for (final destination in bottomItems)
                      NavigationRailDestination(
                        icon: Icon(_iconFor(destination)),
                        label: Text(destination.navigationLabel),
                      ),
                  ],
                ),
                const VerticalDivider(width: 1),
                Expanded(child: _constrainedContent(child)),
              ],
            )
          : _constrainedContent(child),
      bottomNavigationBar: isWide
          ? null
          : NavigationBar(
              selectedIndex: selectedIndex,
              onDestinationSelected: (index) =>
                  context.go(bottomItems[index].path),
              destinations: [
                for (final destination in bottomItems)
                  NavigationDestination(
                    icon: Icon(_iconFor(destination)),
                    label: destination.navigationLabel,
                  ),
              ],
            ),
    );
  }

  Widget _constrainedContent(Widget child) => SafeArea(
    top: false,
    child: Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 960),
        child: child,
      ),
    ),
  );

  IconData _iconFor(AppDestination destination) => switch (destination) {
    AppDestination.dashboard => Icons.home_outlined,
    AppDestination.stockTake => Icons.inventory_2_outlined,
    AppDestination.insights => Icons.insights_outlined,
    AppDestination.forecast => Icons.auto_graph_outlined,
    AppDestination.catalogue => Icons.qr_code_2_outlined,
    AppDestination.notifications => Icons.notifications_none_outlined,
    AppDestination.profile => Icons.person_outline,
    AppDestination.settings => Icons.tune_outlined,
  };
}
