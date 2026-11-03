import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../environments/environment';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { MenuItem, NAV_SECTIONS } from '../models/menu.model';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

function jwt(): string {
  return `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 600 }))}.s`;
}

function leaves(items: MenuItem[]): MenuItem[] {
  return items.flatMap(item => (item.children?.length ? leaves(item.children) : [item]));
}

describe('BusinessOwner wildcard menu visibility', () => {
  const capabilitiesUrl = `${environment.apiUrl}/hr/v1/capabilities`;
  const allLeaves = leaves(NAV_SECTIONS.flatMap(section => section.items));

  function setUp(user: Record<string, unknown>) {
    localStorage.clear();
    localStorage.setItem('auth_token', jwt());
    localStorage.setItem('refresh_token', 'r');
    localStorage.setItem('current_user', JSON.stringify(user));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });
    return {
      menu: TestBed.inject(MenuService),
      controller: TestBed.inject(HttpTestingController),
      auth: TestBed.inject(AuthService)
    };
  }

  const owner = {
    id: 7, businessId: 3, name: 'Owner', email: 'o@x.test', roleName: 'BusinessOwner',
    isBusinessOwner: true, permissions: ['*']
  };
  const visibleIds = (menu: MenuService) => leaves(menu.sections().flatMap(section => section.items)).map(item => item.id);

  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('shows every Business-level menu, including Outlets and Warehouses, without enumerated permissions', () => {
    const { menu, controller } = setUp(owner);
    controller.expectOne(capabilitiesUrl).flush({
      businessId: 3, hrEnabled: true, payrollEnabled: true, attendanceEnabled: true, deviceIntegrationEnabled: true
    });

    const visible = visibleIds(menu);
    const expected = allLeaves.filter(item => item.role !== 'Super Admin').map(item => item.id);
    expect(visible).toEqual(expect.arrayContaining(expected));
    expect(visible).toContain('outlets');
    expect(visible).toContain('warehouses');
    expect(visible).toContain('hr-employees');
    controller.verify();
  });

  it('never shows platform-only menus to a Business owner', () => {
    const { menu, controller } = setUp(owner);
    controller.expectOne(capabilitiesUrl).flush({
      businessId: 3, hrEnabled: true, payrollEnabled: true, attendanceEnabled: true, deviceIntegrationEnabled: true
    });
    expect(visibleIds(menu)).not.toContain('businesses');
  });

  it('keeps HR and Payroll menus hidden while the Business has not enabled those features', () => {
    const { menu, controller } = setUp(owner);
    controller.expectOne(capabilitiesUrl).flush({
      businessId: 3, hrEnabled: false, payrollEnabled: false, attendanceEnabled: false, deviceIntegrationEnabled: false
    });

    const visible = visibleIds(menu);
    const capabilityGated = allLeaves.filter(item => item.capability).map(item => item.id);
    expect(capabilityGated.length).toBeGreaterThan(0);
    for (const id of capabilityGated) {
      expect(visible).not.toContain(id);
    }
    expect(visible).toContain('outlets');
  });

  it('does not load Business capabilities for a Super Admin who has not selected a Business', () => {
    const { controller } = setUp({
      id: 1, name: 'Platform', email: 'p@x.test', roleName: 'Super Admin', realRoleName: 'Super Admin',
      permissions: ['*']
    });
    controller.expectNone(capabilitiesUrl);
  });

  it('shows no permission-gated menus to a user without the wildcard or matching permissions', () => {
    const { menu, controller } = setUp({
      id: 9, businessId: 3, name: 'Cashier', email: 'c@x.test', roleName: 'SalesPerson', permissions: ['sales.create']
    });
    controller.expectNone(capabilitiesUrl);
    const visible = visibleIds(menu);
    expect(visible).not.toContain('outlets');
    expect(visible).not.toContain('users');
    expect(visible).not.toContain('roles');
  });
});
