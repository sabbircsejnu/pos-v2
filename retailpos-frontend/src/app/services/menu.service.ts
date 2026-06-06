import { Injectable, signal } from '@angular/core';
import { MenuItem, MENU_ITEMS } from '../models/menu.model';
import { AuthService } from './auth.service';
import { Subscription } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class MenuService {
  menuItems = signal<MenuItem[]>([]);
  activeParentMenu = signal<MenuItem | null>(null);
  sidebarOpen = signal(true);
  private authSubscription?: Subscription;

  constructor(private authService: AuthService) {
    this.refreshMenu();
    this.authSubscription = this.authService.currentUser$.subscribe(() => {
      this.refreshMenu();
    });
  }

  setActiveParentMenu(menu: MenuItem | null): void {
    this.activeParentMenu.set(menu);
  }

  toggleSidebar(): void {
    this.sidebarOpen.set(!this.sidebarOpen());
  }

  getChildMenuItems(): MenuItem[] {
    const parent = this.activeParentMenu();
    return parent?.children || [];
  }

  private refreshMenu(): void {
    const filteredItems = this.filterMenuItems(MENU_ITEMS);
    this.menuItems.set(filteredItems);

    const currentParent = this.activeParentMenu();
    if (currentParent && !filteredItems.some(item => item.id === currentParent.id)) {
      this.activeParentMenu.set(null);
    }
  }

  private filterMenuItems(items: MenuItem[]): MenuItem[] {
    const result: MenuItem[] = [];

    for (const item of items) {
      const hasOwnAccess = this.hasAccess(item.permission);
      const childItems = item.children ? this.filterMenuItems(item.children) : undefined;

      if (childItems && childItems.length > 0) {
        result.push({ ...item, children: childItems });
        continue;
      }

      if (hasOwnAccess) {
        result.push({ ...item, children: childItems });
      }
    }

    return result;
  }

  private hasAccess(permission?: string | string[]): boolean {
    if (!permission) {
      return true;
    }

    if (Array.isArray(permission)) {
      return this.authService.hasAnyPermission(permission);
    }

    return this.authService.hasPermission(permission);
  }

  // Find and set active parent menu based on current route
  setActiveParentMenuByRoute(route: string): void {
    for (const menuItem of this.menuItems()) {
      if (menuItem.children && menuItem.children.length > 0) {
        // Check if any child route matches the current route
        const matchingChild = menuItem.children.find(child => 
          child.route && route.startsWith(child.route)
        );
        if (matchingChild) {
          this.setActiveParentMenu(menuItem);
          return;
        }
      }
    }
    // No matching parent found, clear active parent
    this.setActiveParentMenu(null);
  }
}
