import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:iranimart_business_mobile/app/navigation/role_navigation.dart';
import 'package:iranimart_business_mobile/app/presentation/authenticated_shell.dart';
import 'package:iranimart_business_mobile/app/presentation/profile_page.dart';
import 'package:iranimart_business_mobile/features/auth/application/auth_session_controller.dart';
import 'package:iranimart_business_mobile/features/auth/presentation/session_loading_page.dart';
import 'package:iranimart_business_mobile/features/auth/presentation/sign_in_page.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/dashboard/presentation/role_dashboard_page.dart';
import 'package:iranimart_business_mobile/features/foundation/presentation/foundation_page.dart';
import 'package:iranimart_business_mobile/features/insights/presentation/sales_insights_page.dart';
import 'package:iranimart_business_mobile/features/catalogue/presentation/catalogue_page.dart';
import 'package:iranimart_business_mobile/features/notifications/presentation/notification_centre_page.dart';
import 'package:iranimart_business_mobile/features/forecast/presentation/sales_forecast_page.dart';
import 'package:iranimart_business_mobile/features/settings/presentation/settings_page.dart';
import 'package:iranimart_business_mobile/core/config/app_environment.dart';
import 'package:iranimart_business_mobile/features/stock_take/domain/stock_take_presentation_models.dart';
import 'package:iranimart_business_mobile/features/stock_take/presentation/stock_take_page.dart';

final appRouterProvider = Provider<GoRouter>((ref) {
  final refresh = _RouterRefresh();
  ref.listen(authSessionControllerProvider, (previous, next) {
    refresh.refresh();
  });
  final router = GoRouter(
    initialLocation: '/',
    refreshListenable: refresh,
    redirect: (context, state) {
      final authState = ref.read(authSessionControllerProvider);
      final path = state.uri.path;
      if (authState.status == AuthStatus.restoring ||
          authState.status == AuthStatus.signingIn ||
          authState.status == AuthStatus.signingOut) {
        return path == '/session' ? null : '/session';
      }

      if (authState.status == AuthStatus.authenticated) {
        if (path == '/' || path == '/login' || path == '/session') {
          return '/dashboard';
        }
        if (_isApplicationDestination(path)) {
          final profile = authState.context!.profile;
          final role = MobileRole.fromServerProfile(
            roleName: profile.roleName,
            isBusinessOwner: profile.isBusinessOwner,
          );
          if (!RoleNavigation.canAccessPath(role, path)) return '/profile';
        }
        return null;
      }

      if (path == '/foundation' || path == '/login') return null;
      return '/login';
    },
    routes: [
      GoRoute(
        path: '/',
        name: 'root',
        redirect: (context, state) => '/dashboard',
      ),
      GoRoute(
        path: '/login',
        name: 'login',
        builder: (context, state) => const SignInPage(),
      ),
      GoRoute(
        path: '/session',
        name: 'session',
        builder: (context, state) => const SessionLoadingPage(),
      ),
      GoRoute(
        path: '/foundation',
        name: 'foundation',
        builder: (context, state) => const FoundationPage(),
      ),
      ShellRoute(
        builder: (context, state, child) =>
            AuthenticatedShell(currentPath: state.uri.path, child: child),
        routes: [
          GoRoute(
            path: '/dashboard',
            name: 'dashboard',
            builder: (context, state) => const DashboardRoutePage(),
          ),
          GoRoute(
            path: '/stock-take',
            name: 'stock-take',
            builder: (context, state) => StockTakeRoutePage(
              selectedSection: StockTakeSection.fromQuery(
                state.uri.queryParameters['section'],
              ),
              onSectionSelected: (section) =>
                  context.go('/stock-take?section=${section.name}'),
            ),
          ),
          GoRoute(
            path: '/insights',
            name: 'insights',
            builder: (context, state) => const InsightsRoutePage(),
          ),
          GoRoute(
            path: '/forecast',
            name: 'forecast',
            builder: (context, state) => const ForecastRoutePage(),
          ),
          GoRoute(
            path: '/catalogue',
            name: 'catalogue',
            builder: (context, state) => const CatalogueRoutePage(),
          ),
          GoRoute(
            path: '/notifications',
            name: 'notifications',
            builder: (context, state) => const NotificationsRoutePage(),
          ),
          GoRoute(
            path: '/profile',
            name: 'profile',
            builder: (context, state) => const ProfilePage(),
          ),
          GoRoute(
            path: '/settings',
            name: 'settings',
            builder: (context, state) => SettingsRoutePage(
              environment: ref.read(appEnvironmentProvider),
            ),
          ),
        ],
      ),
    ],
  );

  ref.onDispose(() {
    router.dispose();
    refresh.dispose();
  });
  return router;
});

bool _isApplicationDestination(String path) =>
    AppDestination.values.any((destination) => destination.path == path);

final class DashboardRoutePage extends ConsumerWidget {
  const DashboardRoutePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    final profile = session.profile;
    final role = MobileRole.fromServerProfile(
      roleName: profile.roleName,
      isBusinessOwner: profile.isBusinessOwner,
    );
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return RoleDashboardPage(
      role: role,
      profileName: profile.name,
      businessId: profile.businessId!,
      outletName: selectedOutlet?.name,
    );
  }
}

final class StockTakeRoutePage extends ConsumerWidget {
  const StockTakeRoutePage({
    required this.selectedSection,
    required this.onSectionSelected,
    super.key,
  });

  final StockTakeSection selectedSection;
  final ValueChanged<StockTakeSection> onSectionSelected;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return StockTakePage(
      role: MobileRole.fromServerProfile(
        roleName: session.profile.roleName,
        isBusinessOwner: session.profile.isBusinessOwner,
      ),
      businessId: session.profile.businessId,
      outletName: selectedOutlet?.name,
      hasInvalidSelectedOutlet:
          session.selectedOutletId != null && selectedOutlet == null,
      selectedSection: selectedSection,
      onSectionSelected: onSectionSelected,
    );
  }
}

final class InsightsRoutePage extends ConsumerWidget {
  const InsightsRoutePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    final profile = session.profile;
    final role = MobileRole.fromServerProfile(
      roleName: profile.roleName,
      isBusinessOwner: profile.isBusinessOwner,
    );
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return SalesInsightsPage(
      role: role,
      profileName: profile.name,
      businessId: profile.businessId!,
      outletName: selectedOutlet?.name,
    );
  }
}

final class ForecastRoutePage extends ConsumerWidget {
  const ForecastRoutePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    final profile = session.profile;
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return SalesForecastPage(
      role: MobileRole.fromServerProfile(
        roleName: profile.roleName,
        isBusinessOwner: profile.isBusinessOwner,
      ),
      businessId: profile.businessId!,
      outletName: selectedOutlet?.name,
    );
  }
}

final class CatalogueRoutePage extends ConsumerWidget {
  const CatalogueRoutePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return CataloguePage(
      businessId: session.profile.businessId!,
      outletName: selectedOutlet?.name,
    );
  }
}

final class NotificationsRoutePage extends ConsumerWidget {
  const NotificationsRoutePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(authSessionControllerProvider).context;
    if (session == null) return const SessionLoadingPage();
    final selectedOutlet = session.outlets
        .where((outlet) => outlet.id == session.selectedOutletId)
        .firstOrNull;
    return NotificationCentrePage(
      businessId: session.profile.businessId!,
      outletName: selectedOutlet?.name,
    );
  }
}

final class SettingsRoutePage extends StatelessWidget {
  const SettingsRoutePage({required this.environment, super.key});

  final AppEnvironment environment;

  @override
  Widget build(BuildContext context) => SettingsPage(environment: environment);
}

final class _RouterRefresh extends ChangeNotifier {
  void refresh() => notifyListeners();
}
