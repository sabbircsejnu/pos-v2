import { NAV_SECTIONS } from '../../models/menu.model';
import { routes } from '../../app.routes';
import { PERMISSION_DETAILS } from '../../components/roles/role-form.component';
import { IdempotencyKeyHolder, defaultLocationKey, locationName, locationOptions, readUomError } from './uom-ui';

describe('readUomError', () => {
  it('reads the stable code through the error interceptor wrapper', () => {
    const wrapped = {
      status: 409,
      message: 'Changed',
      error: { message: 'Changed', error: 'Changed', errors: [] },
      originalError: { error: { message: 'Changed by someone else', errorCode: 'UOM_CONFIG_CHANGED' } },
    };
    expect(readUomError(wrapped, 'fallback')).toEqual({ status: 409, code: 'UOM_CONFIG_CHANGED', message: 'Changed by someone else' });
  });

  it('reads a raw response too', () => {
    const raw = { status: 400, error: { message: 'Bad size', errorCode: 'UOM_INVALID_CONFIGURATION' }, message: 'Http failure response for x: 400' };
    expect(readUomError(raw, 'fallback')).toEqual({ status: 400, code: 'UOM_INVALID_CONFIGURATION', message: 'Bad size' });
  });

  it('never shows the transport message and falls back to the known code text', () => {
    const noBody = { status: 500, error: null, message: 'Http failure response for http://secret-host: 500' };
    expect(readUomError(noBody, 'Try again').message).toBe('Try again');
    const codeOnly = { status: 400, error: { errorCode: 'UOM_INSUFFICIENT_SEALED_STOCK' } };
    expect(readUomError(codeOnly, 'Try again').message).toBe('There are not enough sealed packs.');
  });

  it('reports 403 as access denied whatever the body says', () => {
    expect(readUomError({ status: 403, error: { message: 'Policy x failed' } }, 'fallback').message)
      .toBe('You do not have permission to perform this action.');
  });
});

describe('IdempotencyKeyHolder', () => {
  it('keeps one key across retries of the same payload', () => {
    const holder = new IdempotencyKeyHolder('test');
    const first = holder.keyFor('a');
    expect(first).toMatch(/^test-/);
    expect(holder.keyFor('a')).toBe(first);
  });

  it('issues a new key for a changed payload or after success', () => {
    const holder = new IdempotencyKeyHolder('test');
    const first = holder.keyFor('a');
    const changed = holder.keyFor('b');
    expect(changed).not.toBe(first);
    holder.clear();
    expect(holder.keyFor('b')).not.toBe(changed);
  });
});

describe('location options', () => {
  const access = {
    outlets: [{ id: 1, name: 'Dhanmondi', type: 'outlet' as const }],
    warehouses: [{ id: 1, name: 'Main', type: 'warehouse' as const }],
    isBusinessOwner: false,
  };

  it('keeps an outlet and a warehouse with the same id apart', () => {
    const options = locationOptions(access);
    expect(options.map(option => option.key)).toEqual(['outlet:1', 'warehouse:1']);
    expect(locationName(options, 1, 'warehouse')).toBe('Main');
    expect(locationName(options, 9, 'outlet')).toBe('Outlet #9');
  });

  it('uses the server default only when that location is offered', () => {
    const options = locationOptions(access);
    expect(defaultLocationKey({ ...access, defaultLocationId: 1, defaultLocationType: 'warehouse' }, options)).toBe('warehouse:1');
    expect(defaultLocationKey({ ...access, defaultLocationId: 7, defaultLocationType: 'outlet' }, options)).toBe('');
  });
});

describe('UOM navigation and permissions', () => {
  const keys = ['uom.view', 'uom.manage', 'inventory.pack_state.view', 'inventory.pack_state.manage', 'inventory.repacking.view', 'inventory.repacking.execute'];
  const children = routes.flatMap(route => route.children ?? []);

  it.each([
    ['products/:productId/variants/:variantId/units', 'products.edit'],
    ['uom/units', 'uom.view'],
    ['inventory/pack-state', 'inventory.pack_state.view'],
    ['inventory/repacking', 'inventory.repacking.view'],
  ])('guards %s with %s', (path, permission) => {
    const route = children.find(candidate => candidate.path === path);
    expect(route?.canActivate?.length).toBe(1);
    expect(route?.data?.['permissions']).toEqual([permission]);
  });

  it.each([
    ['/uom/units', 'uom.view'],
    ['/inventory/pack-state', 'inventory.pack_state.view'],
    ['/inventory/repacking', 'inventory.repacking.view'],
  ])('lists %s in the menu with %s', (route, permission) => {
    const item = NAV_SECTIONS.flatMap(section => section.items).find(candidate => candidate.route === route);
    expect(item?.permission).toBe(permission);
  });

  it('gives every new permission a readable label in Role Edit', () => {
    keys.forEach(key => expect(PERMISSION_DETAILS[key]?.label).toBeTruthy());
  });
});
