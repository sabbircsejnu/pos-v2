import { Component, effect, inject, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MenuService } from '../../services/menu.service';
import { MenuItem, MenuSection } from '../../models/menu.model';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent {
  menuService = inject(MenuService);
  private router = inject(Router);
  expandedGroupId = signal<string | null>(null);
  expandedSubGroupIds = signal<ReadonlySet<string>>(new Set<string>());
  private lastSyncedSubGroupRoute: string | null = null;
  private readonly expandedGroupStorageKey = 'sidebar_expanded_group';
  private lastSyncedRoute: string | null = null;

  get sections(): MenuSection[] {
    return this.menuService.sections();
  }

  constructor() {
    this.initializeExpandedGroup();

    effect(() => {
      const activeRoute = this.menuService.activeRoute();
      const sections = this.menuService.sections();
      if (sections.length === 0) {
        return;
      }

      const currentExpanded = this.expandedGroupId();
      const isCurrentValid = !!currentExpanded && sections.some(section => section.id === currentExpanded);
      const routeForSync = activeRoute || this.router.url;

      if (!isCurrentValid) {
        const fallback = this.resolvePreferredExpandedGroup(sections, routeForSync);
        this.setExpandedGroup(fallback);
        this.lastSyncedRoute = routeForSync;
        return;
      }

      if (!routeForSync || this.lastSyncedRoute === routeForSync) {
        return;
      }

      const section = this.findSectionByRoute(routeForSync, sections);
      if (section && this.expandedGroupId() !== section.id) {
        this.setExpandedGroup(section.id);
      }

      this.lastSyncedRoute = routeForSync;
    });

    // Expand the level-2 group owning the current page, including on direct load/reload.
    effect(() => {
      const routeForSync = this.menuService.activeRoute() || this.router.url;
      const groupIds = this.findSubGroupIdsByRoute(routeForSync, this.menuService.sections());
      if (groupIds.length === 0 || this.lastSyncedSubGroupRoute === routeForSync) {
        return;
      }

      untracked(() => this.expandedSubGroupIds.update(current => new Set([...current, ...groupIds])));
      this.lastSyncedSubGroupRoute = routeForSync;
    });
  }

  isSubGroupCollapsed(groupId: string): boolean {
    if (this.menuService.sidebarCollapsed()) {
      return false;
    }

    return !this.expandedSubGroupIds().has(groupId);
  }

  toggleSubGroup(groupId: string): void {
    if (this.menuService.sidebarCollapsed()) {
      return;
    }

    this.expandedSubGroupIds.update(current => {
      const next = new Set(current);
      if (!next.delete(groupId)) {
        next.add(groupId);
      }
      return next;
    });
  }

  isSubGroupActive(group: MenuItem): boolean {
    return (group.children ?? []).some(child => this.isItemActive(child));
  }

  isGroupCollapsed(sectionId: string): boolean {
    if (this.menuService.sidebarCollapsed()) {
      return false;
    }

    return this.expandedGroupId() !== sectionId;
  }

  toggleGroup(sectionId: string): void {
    if (this.menuService.sidebarCollapsed()) {
      return;
    }

    if (this.expandedGroupId() === sectionId) {
      this.setExpandedGroup(null);
      return;
    }

    this.setExpandedGroup(sectionId);
  }

  formatSectionLabel(label: string): string {
    return label
      .toLowerCase()
      .replace(/_/g, ' ')
      .replace(/\b\w/g, (char) => char.toUpperCase());
  }

  isItemActive(item: MenuItem): boolean {
    return this.menuService.isItemActive(item);
  }

  navigateToItem(item: MenuItem): void {
    if (!item.route || item.disabled) {
      return;
    }

    this.router.navigate([item.route]);
    this.menuService.closeMobileSidebar();
  }

  private initializeExpandedGroup(): void {
    const sections = this.menuService.sections();
    if (sections.length === 0) {
      return;
    }

    const initialRoute = this.router.url;
    this.setExpandedGroup(this.resolvePreferredExpandedGroup(sections, initialRoute));
    this.lastSyncedRoute = initialRoute;
  }

  private resolvePreferredExpandedGroup(sections: MenuSection[], route: string): string | null {
    const activeSection = this.findSectionByRoute(route, sections);
    if (activeSection) {
      return activeSection.id;
    }

    const saved = this.readSavedExpandedGroup();
    if (saved && sections.some(section => section.id === saved)) {
      return saved;
    }

    return null;
  }

  private findSectionByRoute(route: string, sections: MenuSection[]): MenuSection | null {
    for (const section of sections) {
      if (this.flattenRoutedItems(section.items).some(item => route.startsWith(item.route!))) {
        return section;
      }
    }

    return null;
  }

  private flattenRoutedItems(items: MenuItem[]): MenuItem[] {
    return items.flatMap(item => [
      ...(item.route ? [item] : []),
      ...(item.children ? this.flattenRoutedItems(item.children) : [])
    ]);
  }

  private findSubGroupIdsByRoute(route: string, sections: MenuSection[]): string[] {
    return sections
      .flatMap(section => section.items)
      .filter(item => item.children?.some(child => !!child.route && route.startsWith(child.route)))
      .map(item => item.id);
  }

  private readSavedExpandedGroup(): string | null {
    if (typeof window === 'undefined') {
      return null;
    }

    return window.localStorage.getItem(this.expandedGroupStorageKey);
  }

  private setExpandedGroup(sectionId: string | null): void {
    this.expandedGroupId.set(sectionId);
    if (typeof window === 'undefined') {
      return;
    }

    if (sectionId) {
      window.localStorage.setItem(this.expandedGroupStorageKey, sectionId);
    } else {
      window.localStorage.removeItem(this.expandedGroupStorageKey);
    }
  }
}
