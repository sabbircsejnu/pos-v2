import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';

void main() {
  group('MobileRole.fromServerProfile', () {
    test('uses the server owner flag as authoritative owner identity', () {
      expect(
        MobileRole.fromServerProfile(
          roleName: 'Administrator',
          isBusinessOwner: true,
        ),
        MobileRole.businessOwner,
      );
    });

    test('recognizes manager and salesperson role labels', () {
      expect(
        MobileRole.fromServerProfile(
          roleName: 'Branch Manager',
          isBusinessOwner: false,
        ),
        MobileRole.manager,
      );
      expect(
        MobileRole.fromServerProfile(
          roleName: 'Cashier',
          isBusinessOwner: false,
        ),
        MobileRole.salesperson,
      );
      expect(
        MobileRole.fromServerProfile(
          roleName: 'Salesperson',
          isBusinessOwner: false,
        ),
        MobileRole.salesperson,
      );
    });

    test('does not grant a dashboard mapping to unknown roles', () {
      expect(
        MobileRole.fromServerProfile(
          roleName: 'Super Admin',
          isBusinessOwner: false,
        ),
        MobileRole.unsupported,
      );
    });
  });
}
