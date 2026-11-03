import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/features/auth/domain/auth_failure.dart';
import 'package:iranimart_business_mobile/features/auth/infrastructure/auth_response_mapper.dart';

void main() {
  const mapper = AuthResponseMapper();

  test('maps only the server-authorized outlet list', () {
    final context = mapper.sessionContext(
      profileJson: _profile(),
      outletsJson: {
        'outlets': [
          {'id': 21, 'name': 'North', 'type': 'outlet'},
        ],
        'warehouses': [
          {'id': 31, 'name': 'Warehouse', 'type': 'warehouse'},
        ],
        'defaultOutletId': 21,
      },
      expectedUserId: 4,
    );

    expect(context.outlets.map((outlet) => outlet.id), [21]);
    expect(context.selectedOutletId, 21);
  });

  test('does not select an outlet outside the authorized result', () {
    final context = mapper.sessionContext(
      profileJson: _profile(),
      outletsJson: {
        'outlets': [
          {'id': 21, 'name': 'North', 'type': 'outlet'},
          {'id': 22, 'name': 'South', 'type': 'outlet'},
        ],
        'defaultOutletId': 99,
      },
      expectedUserId: 4,
    );

    expect(context.selectedOutletId, isNull);
  });

  test('rejects an arbitrary client-selected outlet', () {
    final context = mapper.sessionContext(
      profileJson: _profile(),
      outletsJson: {
        'outlets': [
          {'id': 21, 'name': 'North', 'type': 'outlet'},
        ],
      },
      expectedUserId: 4,
    );

    expect(
      () => context.selectOutlet(99),
      throwsA(
        isA<AuthFailure>().having(
          (failure) => failure.kind,
          'kind',
          AuthFailureKind.forbidden,
        ),
      ),
    );
  });

  test(
    'rejects missing Business context and role-switched or Super Admin users',
    () {
      for (final profile in [
        _profile()..['businessId'] = null,
        _profile()..['roleName'] = 'Super Admin',
        _profile()..['isRoleSwitched'] = true,
      ]) {
        expect(
          () => mapper.sessionContext(
            profileJson: profile,
            outletsJson: {'outlets': []},
            expectedUserId: 4,
          ),
          throwsA(isA<AuthFailure>()),
        );
      }
    },
  );

  test('requires interactive invitation/password setup outside this slice', () {
    expect(
      () => mapper.sessionContext(
        profileJson: _profile()..['mustResetPassword'] = true,
        outletsJson: {'outlets': []},
        expectedUserId: 4,
      ),
      throwsA(
        isA<AuthFailure>().having(
          (failure) => failure.kind,
          'kind',
          AuthFailureKind.passwordSetupRequired,
        ),
      ),
    );
  });
}

Map<String, dynamic> _profile() => {
  'id': 4,
  'businessId': 8,
  'name': 'A User',
  'email': 'user@example.test',
  'roleName': 'Business Owner',
  'isRoleSwitched': false,
  'isBusinessOwner': true,
  'mustResetPassword': false,
};
