import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';

enum AppDestination {
  dashboard('/dashboard', 'Home', 'Dashboard', 'Home'),
  stockTake('/stock-take', 'Stock Take', 'Stock Take', 'Stock Take'),
  insights('/insights', 'Insights', 'Insights', 'Sales Insights'),
  forecast('/forecast', 'Forecast', 'Forecast', 'Sales Forecasting'),
  catalogue('/catalogue', 'Catalogue', 'Catalogue', 'Product Catalogue'),
  notifications('/notifications', 'Alerts', 'Notifications', 'Notifications'),
  profile('/profile', 'Profile', 'Profile', 'Profile'),
  settings('/settings', 'Settings', 'Settings', 'Settings');

  const AppDestination(
    this.path,
    this.navigationLabel,
    this.menuLabel,
    this.pageTitle,
  );

  final String path;
  final String navigationLabel;
  final String menuLabel;
  final String pageTitle;
}

abstract final class RoleNavigation {
  static const _ownerDestinations = [
    AppDestination.dashboard,
    AppDestination.stockTake,
    AppDestination.insights,
    AppDestination.forecast,
    AppDestination.catalogue,
    AppDestination.notifications,
    AppDestination.profile,
    AppDestination.settings,
  ];

  static const _managerDestinations = [
    AppDestination.dashboard,
    AppDestination.stockTake,
    AppDestination.insights,
    AppDestination.catalogue,
    AppDestination.notifications,
    AppDestination.profile,
    AppDestination.settings,
  ];

  static const _salespersonDestinations = [
    AppDestination.dashboard,
    AppDestination.stockTake,
    AppDestination.insights,
    AppDestination.notifications,
    AppDestination.profile,
    AppDestination.settings,
  ];

  static const _ownerBottomNavigation = [
    AppDestination.dashboard,
    AppDestination.stockTake,
    AppDestination.insights,
    AppDestination.profile,
  ];

  static const _salespersonBottomNavigation = [
    AppDestination.dashboard,
    AppDestination.stockTake,
    AppDestination.notifications,
    AppDestination.profile,
  ];

  static List<AppDestination> destinationsFor(MobileRole role) =>
      switch (role) {
        MobileRole.businessOwner => _ownerDestinations,
        MobileRole.manager => _managerDestinations,
        MobileRole.salesperson => _salespersonDestinations,
        MobileRole.unsupported => const [AppDestination.profile],
      };

  static List<AppDestination> bottomNavigationFor(MobileRole role) =>
      switch (role) {
        MobileRole.businessOwner ||
        MobileRole.manager => _ownerBottomNavigation,
        MobileRole.salesperson => _salespersonBottomNavigation,
        MobileRole.unsupported => const [AppDestination.profile],
      };

  static List<AppDestination> moreDestinationsFor(MobileRole role) {
    final bottom = bottomNavigationFor(role).toSet();
    return destinationsFor(role)
        .where((destination) => !bottom.contains(destination))
        .toList(growable: false);
  }

  static bool canAccessPath(MobileRole role, String path) =>
      destinationsFor(role).any((destination) => destination.path == path);
}
