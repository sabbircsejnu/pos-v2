import { Injectable, signal } from '@angular/core';
import { MenuItem, MenuSection, NAV_SECTIONS } from '../models/menu.model';
import { AuthService } from './auth.service';
import { Subscription } from 'rxjs';

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

  constructor(private authService: AuthService) {
    this.sidebarDensity.set(this.readSavedDensity());
    this.refreshMenu();
    this.authSubscription = this.authService.currentUser$.subscribe(() => {
      this.refreshMenu();
    });
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
      if (item.children && item.children.length > 0) {
        const childItems = this.filterMenuItems(item.children);
        if (childItems.length > 0 && this.hasAccess(item.permission, item.role)) {
          filtered.push({ ...item, children: childItems });
        }
        continue;
      }

      if (this.hasAccess(item.permission, item.role)) {
        filtered.push(item);
      }
    }

    return filtered;
  }

  private hasAccess(permission?: string | string[], role?: string | string[]): boolean {
    const roleAllowed = this.hasRoleAccess(role);
    if (!roleAllowed) {
      return false;
    }

    if (!permission) {
      return true;
    }

    if (Array.isArray(permission)) {
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

  private readSavedDensity(): SidebarDensity {
    if (typeof window === 'undefined') {
      return 'compact';
    }

    const saved = window.localStorage.getItem('sidebar_density');
    return saved === 'comfortable' ? 'comfortable' : 'compact';
  }
}
