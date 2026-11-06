import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { User, UserContextOptions } from '../../models/auth.models';
import { AuthService } from '../../services/auth.service';
import { MenuService } from '../../services/menu.service';
import { NavbarComponent } from './navbar.component';

const OWNER: User = {
  id: 1,
  name: 'Olivia Owner',
  email: 'olivia@example.test',
  roleName: 'BusinessOwner',
  permissions: ['*'],
  isBusinessOwner: true
} as User;

const OWNER_OPTIONS: UserContextOptions = {
  canSwitch: true,
  allowAllLocations: true,
  roles: [{ id: 1, name: 'Cashier' }, { id: 2, name: 'OutletManager' }],
  outlets: [{ id: 5, name: 'Main Outlet' }, { id: 6, name: 'Second Outlet' }],
  warehouses: [{ id: 9, name: 'Main Warehouse' }]
};

describe('NavbarComponent presentation', () => {
  let fixture: ComponentFixture<NavbarComponent>;
  let user$: BehaviorSubject<User | null>;
  let auth: {
    currentUser$: BehaviorSubject<User | null>;
    getSwitchOptions: ReturnType<typeof vi.fn>;
    switchRole: ReturnType<typeof vi.fn>;
    returnOwnerMode: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
  };
  let menu: { toggleSidebar: ReturnType<typeof vi.fn> };
  let router: Router;

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement>(selector: string) => root().querySelector(selector) as T;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    user$ = new BehaviorSubject<User | null>(OWNER);
    auth = {
      currentUser$: user$,
      getSwitchOptions: vi.fn(() => of(OWNER_OPTIONS)),
      switchRole: vi.fn(() => of({})),
      returnOwnerMode: vi.fn(() => of({})),
      logout: vi.fn()
    };
    menu = { toggleSidebar: vi.fn() };

    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: MenuService, useValue: menu }
      ]
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(NavbarComponent);
    await settle();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('names every icon-only control for assistive technology', () => {
    expect(q('button[aria-controls="app-sidebar"]').getAttribute('aria-label')).toBe('Toggle navigation menu');
    expect(q('button[aria-label="Notifications"]')).not.toBeNull();
    expect(q('button[aria-label="Outlet switcher"]')).not.toBeNull();
    expect(q('button[aria-label="User menu"]')).not.toBeNull();
    expect(q('input[name="globalSearch"]').getAttribute('aria-label')).toBeTruthy();
  });

  it('toggles the sidebar through the existing menu service', () => {
    q<HTMLButtonElement>('button[aria-controls="app-sidebar"]').click();
    expect(menu.toggleSidebar).toHaveBeenCalledTimes(1);
  });

  it('runs a trimmed global search and ignores an empty query', async () => {
    const input = q<HTMLInputElement>('input[name="globalSearch"]');
    const form = q<HTMLFormElement>('form[role="search"]');

    form.dispatchEvent(new Event('submit'));
    expect(router.navigate).not.toHaveBeenCalled();

    input.value = '  rice  ';
    input.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    expect(router.navigate).toHaveBeenCalledWith(['/search'], { queryParams: { q: 'rice' } });
  });

  it('opens the user menu, exposes its state, and closes it with Escape', async () => {
    const trigger = q<HTMLButtonElement>('button[aria-label="User menu"]');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(root().querySelector('#user-menu-panel')).toBeNull();

    trigger.click();
    await settle();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(q('#user-menu-panel').textContent).toContain('olivia@example.test');

    q('header').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(root().querySelector('#user-menu-panel')).toBeNull();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
  });

  it('logs out through AuthService and returns to the login page', async () => {
    q<HTMLButtonElement>('button[aria-label="User menu"]').click();
    await settle();
    q<HTMLButtonElement>('#user-menu-panel button.rp-menu-item--danger').click();

    expect(auth.logout).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('keeps the outlet switcher fields labelled and the action disabled until a role is chosen', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();

    expect(q('label[for="acting-role-select"]')).not.toBeNull();
    expect(q('label[for="acting-location-type-select"]')).not.toBeNull();
    expect(q('label[for="acting-location-select"]')).not.toBeNull();
    const apply = q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary');
    expect(apply.disabled).toBe(true);
    expect(q('#outlet-switcher-panel').textContent).toContain('Owner Mode (All Outlets)');
  });

  const selectValue = async (selector: string, value: string) => {
    const select = q<HTMLSelectElement>(selector);
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await settle();
  };

  const selectLocation = async (name: string) => {
    const select = q<HTMLSelectElement>('#acting-location-select');
    const option = Array.from(select.options).find(candidate => candidate.text.trim() === name)!;
    select.value = option.value;
    select.dispatchEvent(new Event('change'));
    await settle();
  };

  it('offers only the roles and locations returned by the server', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();

    const roles = Array.from(q<HTMLSelectElement>('#acting-role-select').options).map(o => o.text.trim());
    expect(roles).toEqual(['Select role', 'Cashier', 'OutletManager']);
    const types = Array.from(q<HTMLSelectElement>('#acting-location-type-select').options).map(o => o.text.trim());
    expect(types).toEqual(['Outlet', 'Warehouse', 'All Locations']);
  });

  it('requires an outlet, then switches with the chosen location', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();
    await selectValue('#acting-role-select', 'Cashier');

    const apply = q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary');
    expect(apply.disabled).toBe(true);

    await selectLocation('Second Outlet');
    expect(apply.disabled).toBe(false);
    apply.click();

    expect(auth.switchRole).toHaveBeenCalledWith({
      actingRole: 'Cashier',
      actingLocationType: 'outlet',
      actingLocationId: 6
    });
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });

  it('auto-selects a single warehouse after choosing the warehouse type', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();
    await selectValue('#acting-role-select', 'Cashier');
    await selectValue('#acting-location-type-select', 'warehouse');
    await settle();

    q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary').click();

    expect(auth.switchRole).toHaveBeenCalledWith({
      actingRole: 'Cashier',
      actingLocationType: 'warehouse',
      actingLocationId: 9
    });
  });

  it('activates the All Locations context without naming a location', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();
    await selectValue('#acting-role-select', 'Cashier');
    await selectValue('#acting-location-type-select', 'all');

    expect(root().querySelector('#acting-location-select')).toBeNull();
    q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary').click();

    expect(auth.switchRole).toHaveBeenCalledWith({
      actingRole: 'Cashier',
      actingLocationType: 'all',
      actingLocationId: undefined
    });
  });

  it('hides All Locations when the server does not allow it', async () => {
    auth.getSwitchOptions.mockReturnValue(of({
      ...OWNER_OPTIONS,
      allowAllLocations: false,
      roles: [{ id: 3, name: 'Warehouse Clerk' }],
      outlets: [],
      warehouses: [{ id: 9, name: 'Main Warehouse' }]
    }));
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();
    await fixture.whenStable();
    await settle();

    const types = Array.from(q<HTMLSelectElement>('#acting-location-type-select').options).map(o => o.text.trim());
    expect(types).toEqual(['Warehouse']);

    // One role and one warehouse: the only valid context is ready to activate.
    expect(q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary').disabled).toBe(false);
  });

  it('hides the switcher from a user with nothing to switch to', async () => {
    auth.getSwitchOptions.mockReturnValue(of({
      canSwitch: false,
      allowAllLocations: false,
      roles: [],
      outlets: [],
      warehouses: []
    }));
    user$.next({ ...OWNER, id: 2, isBusinessOwner: false, roleName: 'Cashier' });
    await settle();

    expect(root().querySelector('button[aria-label="Outlet switcher"]')).toBeNull();
  });

  it('lets a switched user whose access was removed return to their own role', async () => {
    auth.getSwitchOptions.mockReturnValue(of({
      canSwitch: false,
      allowAllLocations: false,
      roles: [],
      outlets: [],
      warehouses: []
    }));
    user$.next({
      ...OWNER,
      id: 2,
      isBusinessOwner: false,
      isRoleSwitched: true,
      actingRoleName: 'Cashier',
      actingWarehouseName: 'Main Warehouse'
    });
    await settle();

    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();

    expect(q('#outlet-switcher-panel').textContent).toContain('Cashier @ Main Warehouse');
    expect(root().querySelector('#acting-role-select')).toBeNull();
    const back = q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--success');
    expect(back.textContent).toContain('Return to My Role');
    back.click();
    expect(auth.returnOwnerMode).toHaveBeenCalledTimes(1);
  });

  it('shows access denied without logging out when the server refuses the switch', async () => {
    auth.switchRole.mockReturnValue(throwError(() => ({ status: 403 })));
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();
    await selectValue('#acting-role-select', 'Cashier');
    await selectValue('#acting-location-type-select', 'all');
    q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary').click();
    await settle();

    expect(q('#outlet-switcher-panel [role="alert"]').textContent).toContain('not allowed');
    expect(auth.logout).not.toHaveBeenCalled();
  });

  it('announces a failed role switch as an alert without leaving the page', async () => {
    auth.switchRole.mockReturnValue(throwError(() => ({ error: { message: 'Not allowed' } })));
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();

    await selectValue('#acting-role-select', 'Cashier');
    await selectValue('#acting-location-type-select', 'all');
    q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary').click();
    await settle();

    expect(q('#outlet-switcher-panel [role="alert"]').textContent).toContain('Not allowed');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('offers Return Owner Mode only while a role is switched and calls the existing service', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();
    expect(root().querySelector('#outlet-switcher-panel button.rp-btn--success')).toBeNull();

    user$.next({ ...OWNER, isRoleSwitched: true, actingRoleName: 'Cashier', actingOutletName: 'Main Outlet' });
    await settle();
    const back = q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--success');
    expect(q('#outlet-switcher-panel').textContent).toContain('Cashier @ Main Outlet');

    back.click();
    expect(auth.returnOwnerMode).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });
});
