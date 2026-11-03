import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Route, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { routes } from '../../app.routes';
import { NAV_SECTIONS } from '../../models/menu.model';
import { routePermissionGuard } from '../../guards/auth.guard';
import { PosDiscountSettingsComponent } from './pos-discount-settings.component';
import { SettingsPageComponent } from './settings-page.component';

function find(list: Route[], path: string): Route | undefined {
  for (const route of list) {
    if (route.path === path) return route;
    const nested = route.children ? find(route.children, path) : undefined;
    if (nested) return nested;
  }
  return undefined;
}

describe('settings and discount-limit routes', () => {
  const settingsRoute = find(routes, 'settings')!;
  const discountRoute = find(routes, 'settings/discounts')!;

  function allowed(route: Route, permissions: string[]): boolean {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser$: of(null),
            getUserValue: () => null,
            isAuthenticated: () => true,
            isSuperAdmin: () => false,
            hasAnyPermission: (required: string[]) => required.some(p => permissions.includes(p)),
            hasAllPermissions: (required: string[]) => required.every(p => permissions.includes(p))
          }
        }
      ]
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const result = TestBed.runInInjectionContext(() =>
      routePermissionGuard({ data: route.data } as unknown as ActivatedRouteSnapshot, { url: '/x' } as RouterStateSnapshot));
    if (result !== true) {
      expect(navigate).toHaveBeenCalledWith(['/dashboard'], { queryParams: { unauthorized: '1' } });
    }
    return result === true;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('maps the full Settings page and the dedicated discount screen to their own permissions', () => {
    expect(settingsRoute.component).toBe(SettingsPageComponent);
    expect(settingsRoute.data?.['permissions']).toEqual(['settings.edit']);
    expect(settingsRoute.canActivate).toContain(routePermissionGuard);
    expect(discountRoute.component).toBe(PosDiscountSettingsComponent);
    expect(discountRoute.data?.['permissions']).toEqual(['sales.discount.configure']);
    expect(discountRoute.canActivate).toContain(routePermissionGuard);
  });

  it('lets a sales.discount.configure-only user reach the discount screen but not Settings', () => {
    expect(allowed(discountRoute, ['sales.discount.configure'])).toBe(true);
    TestBed.resetTestingModule();
    expect(allowed(settingsRoute, ['sales.discount.configure'])).toBe(false);
    TestBed.resetTestingModule();
    expect(allowed(find(routes, 'settings/company')!, ['sales.discount.configure'])).toBe(false);
  });

  it('lets a settings.edit-only user reach Settings but not the discount screen', () => {
    expect(allowed(settingsRoute, ['settings.edit'])).toBe(true);
    TestBed.resetTestingModule();
    expect(allowed(discountRoute, ['settings.edit'])).toBe(false);
  });

  it('advertises the discount screen in the menu for sales.discount.configure', () => {
    const items = NAV_SECTIONS.flatMap(section => section.items);
    const entry = items.find(item => item.route === '/settings/discounts');
    expect(entry?.permission).toBe('sales.discount.configure');
    expect(items.find(item => item.route === '/settings')?.permission).toBe('settings.edit');
  });
});
