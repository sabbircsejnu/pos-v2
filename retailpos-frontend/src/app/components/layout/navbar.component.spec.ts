import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { User } from '../../models/auth.models';
import { AuthService } from '../../services/auth.service';
import { MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { NavbarComponent } from './navbar.component';

const OWNER: User = {
  id: 1,
  name: 'Olivia Owner',
  email: 'olivia@example.test',
  roleName: 'BusinessOwner',
  permissions: ['*'],
  isBusinessOwner: true
} as User;

describe('NavbarComponent presentation', () => {
  let fixture: ComponentFixture<NavbarComponent>;
  let user$: BehaviorSubject<User | null>;
  let auth: {
    currentUser$: BehaviorSubject<User | null>;
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
        { provide: MenuService, useValue: menu },
        { provide: OutletService, useValue: { getAllOutlets: () => of({ data: [{ id: 5, name: 'Main Outlet' }] }) } }
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
    expect(q('label[for="acting-outlet-select"]')).not.toBeNull();
    const apply = q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary');
    expect(apply.disabled).toBe(true);
    expect(q('#outlet-switcher-panel').textContent).toContain('Owner Mode (All Outlets)');
  });

  it('switches role and outlet with the unchanged request payload', async () => {
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();

    const role = q<HTMLSelectElement>('#acting-role-select');
    role.value = 'Cashier';
    role.dispatchEvent(new Event('change'));
    await settle();

    const apply = q<HTMLButtonElement>('#outlet-switcher-panel button.rp-btn--primary');
    expect(apply.disabled).toBe(false);
    apply.click();

    expect(auth.switchRole).toHaveBeenCalledWith({ actingRole: 'Cashier', actingOutletId: undefined });
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });

  it('announces a failed role switch as an alert without leaving the page', async () => {
    auth.switchRole.mockReturnValue(throwError(() => ({ error: { message: 'Not allowed' } })));
    q<HTMLButtonElement>('button[aria-label="Outlet switcher"]').click();
    await settle();

    const role = q<HTMLSelectElement>('#acting-role-select');
    role.value = 'Cashier';
    role.dispatchEvent(new Event('change'));
    await settle();
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
