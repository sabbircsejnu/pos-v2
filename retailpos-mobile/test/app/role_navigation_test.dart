import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/app/navigation/role_navigation.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';

void main() {
  group('RoleNavigation', () {
    test(
      'completed destinations use their screen titles, not preview labels',
      () {
        expect(AppDestination.insights.pageTitle, 'Sales Insights');
        expect(AppDestination.forecast.pageTitle, 'Sales Forecasting');
        expect(AppDestination.catalogue.pageTitle, 'Product Catalogue');
        expect(AppDestination.notifications.pageTitle, 'Notifications');
        expect(AppDestination.settings.pageTitle, 'Settings');
        for (final destination in AppDestination.values) {
          expect(destination.pageTitle, isNot(contains('preview')));
        }
      },
    );

    test('owner sees owner destinations and four primary tabs', () {
      expect(RoleNavigation.bottomNavigationFor(MobileRole.businessOwner), [
        AppDestination.dashboard,
        AppDestination.stockTake,
        AppDestination.insights,
        AppDestination.profile,
      ]);
      expect(RoleNavigation.moreDestinationsFor(MobileRole.businessOwner), [
        AppDestination.forecast,
        AppDestination.catalogue,
        AppDestination.notifications,
        AppDestination.settings,
      ]);
    });

    test('manager has insights but not forecasting', () {
      expect(
        RoleNavigation.canAccessPath(MobileRole.manager, '/stock-take'),
        isTrue,
      );
      expect(
        RoleNavigation.canAccessPath(MobileRole.manager, '/insights'),
        isTrue,
      );
      expect(
        RoleNavigation.canAccessPath(MobileRole.manager, '/forecast'),
        isFalse,
      );
      expect(RoleNavigation.moreDestinationsFor(MobileRole.manager), [
        AppDestination.catalogue,
        AppDestination.notifications,
        AppDestination.settings,
      ]);
    });

    test('cashier and salesperson share restricted mobile navigation', () {
      expect(RoleNavigation.destinationsFor(MobileRole.salesperson), [
        AppDestination.dashboard,
        AppDestination.stockTake,
        AppDestination.insights,
        AppDestination.notifications,
        AppDestination.profile,
        AppDestination.settings,
      ]);
      expect(
        RoleNavigation.canAccessPath(MobileRole.salesperson, '/stock-take'),
        isTrue,
      );
      expect(
        RoleNavigation.canAccessPath(MobileRole.salesperson, '/forecast'),
        isFalse,
      );
      expect(
        RoleNavigation.canAccessPath(MobileRole.salesperson, '/insights'),
        isTrue,
      );
      expect(
        RoleNavigation.canAccessPath(MobileRole.salesperson, '/catalogue'),
        isFalse,
      );
      expect(RoleNavigation.moreDestinationsFor(MobileRole.salesperson), [
        AppDestination.insights,
        AppDestination.settings,
      ]);
    });

    test('unknown roles retain profile access only', () {
      expect(RoleNavigation.destinationsFor(MobileRole.unsupported), [
        AppDestination.profile,
      ]);
      expect(
        RoleNavigation.canAccessPath(MobileRole.unsupported, '/dashboard'),
        isFalse,
      );
    });
  });
}
