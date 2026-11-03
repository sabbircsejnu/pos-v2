import { Pipe, PipeTransform } from '@angular/core';
import * as _ from 'lodash'; // Import lodash library
import { NavigationItem } from '../types/navigation';

@Pipe({
  name: 'searchFilter'
})
export class SearchFilterPipe implements PipeTransform {
  transform(menus: NavigationItem[], query: string) {
    if (query) {
      return this.filterMenus(menus, query.toLowerCase());
    }
    return menus;
  }

  private filterMenus(menus: NavigationItem[], query: string): NavigationItem[] {
    const filteredMenus: NavigationItem[] = [];

    for (const menu of menus) {
      if (_.includes(menu.title?.toLowerCase(), query)) {
        // Use lodash's includes method
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
