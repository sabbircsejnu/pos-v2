import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { MenuItem, MenuSection } from '../../models/menu.model';
import { MenuService } from '../../services/menu.service';

interface SearchResultItem {
  sectionId: string;
  sectionLabel: string;
  item: MenuItem;
}

@Component({
  selector: 'app-global-search',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './global-search.component.html',
  styleUrls: ['./global-search.component.css']
})
export class GlobalSearchComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private menuService = inject(MenuService);

  query = signal('');

  sections = computed<MenuSection[]>(() => this.menuService.sections());

  results = computed<SearchResultItem[]>(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) {
      return [];
    }

    const items: SearchResultItem[] = [];
    for (const section of this.sections()) {
      for (const item of section.items) {
        if (!item.route || item.disabled) {
          continue;
        }

        const haystack = `${item.label} ${section.label}`.toLowerCase();
        if (haystack.includes(q)) {
          items.push({
            sectionId: section.id,
            sectionLabel: section.label,
            item,
          });
        }
      }
    }

    return items.slice(0, 24);
  });

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      const q = params.get('q') ?? '';
      this.query.set(q);
    });
  }

  updateQuery(value: string): void {
    this.query.set(value);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: value || null },
      queryParamsHandling: 'merge'
    });
  }

  navigateTo(route: string): void {
    this.router.navigate([route]);
  }
}
