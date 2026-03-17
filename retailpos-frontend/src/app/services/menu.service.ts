import { Injectable, signal } from '@angular/core';
import { MenuItem, MENU_ITEMS } from '../models/menu.model';

@Injectable({
  providedIn: 'root'
})
export class MenuService {
  menuItems = signal<MenuItem[]>(MENU_ITEMS);
  activeParentMenu = signal<MenuItem | null>(null);
  sidebarOpen = signal(true);

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
