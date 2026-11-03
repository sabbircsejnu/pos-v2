import { Injectable, signal } from '@angular/core';
import { MenuItem, MenuSection, NAV_SECTIONS } from '../models/menu.model';
import { AuthService } from './auth.service';
import { BusinessContextStore } from './business-context.store';
import { Subscription } from 'rxjs';
import { HrPayrollCapabilityStore } from './hr-payroll-capability.store';
import { HrPayrollCapabilityService } from './hr-payroll-capability.service';

export type SidebarDensity = 'comfortable' | 'compact';

@Injectable({
  providedIn: 'root'
})
export class MenuService {
  sections = signal<MenuSection[]>([]);
  sidebarCollapsed = signal(false);
  mobileSidebarOpen = signal(false);
  sidebarDensity = signal<SidebarDensity>('compact');
  activeRoute = signal<string>('');
  private authSubscription?: Subscription;
  private contextSubscription?: Subscription;
  private capabilitySubscription?: Subscription;
  private capabilityLoad?: Subscription;

  constructor(private authService: AuthService, private businessContextStore: BusinessContextStore,
    private hrPayrollCapabilities: HrPayrollCapabilityStore,
    private capabilityApi: HrPayrollCapabilityService) {
    this.sidebarDensity.set(this.readSavedDensity());
    this.refreshMenu();
    // Both streams replay their current value on subscribe; load capabilities once afterwards.
    let initialising = true;
    this.authSubscription = this.authService.currentUser$.subscribe(() => {
      this.hrPayrollCapabilities.reset();
      this.refreshMenu();
      if (!initialising) this.loadCapabilities();
    });
    this.contextSubscription = this.businessContextStore.selected$.subscribe(() => {
      this.hrPayrollCapabilities.reset();
      this.refreshMenu();
      if (!initialising) this.loadCapabilities();
    });
    this.capabilitySubscription = this.hrPayrollCapabilities.changed$.subscribe(() => this.refreshMenu());
    initialising = false;
    this.loadCapabilities();
  }

  toggleSidebar(): void {
    if (typeof window !== 'undefined' && window.innerWidth < 1024) {
      this.mobileSidebarOpen.set(!this.mobileSidebarOpen());
      return;
    }

    this.sidebarCollapsed.set(!this.sidebarCollapsed());
  }

  closeMobileSidebar(): void {
    this.mobileSidebarOpen.set(false);
  }

  setActiveRouteByUrl(url: string): void {
    this.activeRoute.set(url);
  }

  setSidebarDensity(density: SidebarDensity): void {
    this.sidebarDensity.set(density);
    if (typeof window !== 'undefined') {
      window.localStorage.setItem('sidebar_density', density);
    }
  }

  isItemActive(item: MenuItem): boolean {
    if (!item.route) return false;
    const current = this.activeRoute();
    if (item.route === '/dashboard') {
      return current === '/dashboard';
    }
    return current.startsWith(item.route);
  }

  /**
   * HR/Payroll menu entries depend on the Business's feature entitlements, which only the backend
   * knows. Load them in the background whenever the user/Business scope changes so those entries
   * appear without first having to open an HR route (which is unreachable while hidden).
   */
  private loadCapabilities(): void {
    this.capabilityLoad?.unsubscribe();
    const user = this.authService.getUserValue();
    if (!user || !this.authService.isAuthenticated() || this.isSuperAdminWithoutBusinessContext()) {
      return;
    }
    if (!this.hasCapabilityGatedMenuAccess(NAV_SECTIONS.flatMap(section => section.items))) {
      return;
    }

    const generation = this.hrPayrollCapabilities.generation;
    this.capabilityLoad = this.capabilityApi.load().subscribe({
      next: value => {
        if (this.hrPayrollCapabilities.generation !== generation) {
          return;
        }
        try {
          this.hrPayrollCapabilities.setVerified(value);
        } catch {
          // An unverifiable response leaves the capability menus hidden.
        }
      },
      error: () => undefined
    });
  }

  private hasCapabilityGatedMenuAccess(items: MenuItem[]): boolean {
    return items.some(item =>
      (item.capability && this.hasAccess(item.permission, item.role, item.permissionMode))
      || (item.children?.length ? this.hasCapabilityGatedMenuAccess(item.children) : false));
  }

  private refreshMenu(): void {
    const filteredSections = this.filterMenuSections(NAV_SECTIONS);
    this.sections.set(filteredSections);
  }

  private filterMenuSections(sections: MenuSection[]): MenuSection[] {
    const result: MenuSection[] = [];

    for (const section of sections) {
      const items = this.filterMenuItems(section.items);
      if (items.length > 0) {
        result.push({ ...section, items });
      }
    }

    return result;
  }

  private filterMenuItems(items: MenuItem[]): MenuItem[] {
    const filtered: MenuItem[] = [];

    for (const item of items) {
      if (item.capability && !this.hrPayrollCapabilities.isEnabled(item.capability)) {
        continue;
      }
      if (item.children && item.children.length > 0) {
        const childItems = this.filterMenuItems(item.children);
        if (childItems.length > 0 && this.hasAccess(item.permission, item.role, item.permissionMode)) {
          filtered.push({ ...item, children: childItems });
        }
        continue;
      }

      if (this.hasAccess(item.permission, item.role, item.permissionMode)) {
        filtered.push(item);
      }
    }

    return filtered;
  }

  private hasAccess(
    permission?: string | string[],
    role?: string | string[],
    permissionMode: 'all' | 'any' = 'any'
  ): boolean {
    const roleAllowed = this.hasRoleAccess(role);
    if (!roleAllowed) {
      return false;
    }

    if (!permission) {
      return true;
    }

    // A Super Admin has no Business scope until one is explicitly selected and server-verified,
    // so Business-scoped (permission-gated) workflows stay hidden until then.
    if (this.isSuperAdminWithoutBusinessContext()) {
      return false;
    }

    if (Array.isArray(permission)) {
      if (permissionMode === 'all') {
        return permission.every(required => this.authService.hasPermission(required));
      }
      return this.authService.hasAnyPermission(permission);
    }

    return this.authService.hasPermission(permission);
  }

  private hasRoleAccess(role?: string | string[]): boolean {
    if (!role) {
      return true;
    }

    if (Array.isArray(role)) {
      return role.some(r => this.authService.hasRole(r));
    }

    return this.authService.hasRole(role);
  }

  private isSuperAdminWithoutBusinessContext(): boolean {
    return this.authService.hasRole('Super Admin') && this.businessContextStore.current() === null;
  }

  private readSavedDensity(): SidebarDensity {
    if (typeof window === 'undefined') {
      return 'compact';
    }

    const saved = window.localStorage.getItem('sidebar_density');
    return saved === 'comfortable' ? 'comfortable' : 'compact';
  }
}
