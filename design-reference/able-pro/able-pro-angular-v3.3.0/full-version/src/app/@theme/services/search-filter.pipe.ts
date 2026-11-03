import { Pipe, PipeTransform } from '@angular/core';
import { NavigationItem } from '../types/navigation';

@Pipe({
  name: 'searchFilter',
  standalone: true
})
export class SearchFilterPipe implements PipeTransform {
  transform(menus: NavigationItem[], query: string): NavigationItem[] {
    if (!query || query.trim() === '') {
      return menus;
    }
    return this.filterMenus(menus, query.toLowerCase());
  }

  private filterMenus(menus: NavigationItem[], query: string): NavigationItem[] {
    const filteredMenus: NavigationItem[] = [];

    for (const menu of menus) {
      // Use native string includes instead of lodash
      if (menu.title?.toLowerCase().includes(query)) {
        filteredMenus.push(menu);
      }

      if (menu.children) {
        const filteredChildren = this.filterMenus(menu.children, query);
        if (filteredChildren.length > 0) {
          // If any child matches the query, add the parent item to the results
          filteredMenus.push({ ...menu, children: filteredChildren });
        }
      }
    }

    return filteredMenus;
  }
}
