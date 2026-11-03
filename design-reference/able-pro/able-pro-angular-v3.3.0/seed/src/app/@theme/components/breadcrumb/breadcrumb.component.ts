// Angular Import
import { Component, inject, Input, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterModule, Event } from '@angular/router';
import { Title } from '@angular/platform-browser';

// project import
import { NavigationItem } from '../../types/navigation';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { menus } from 'src/app/demo/data/menu';
import { componentMenus } from 'src/app/demo/data/component';

interface titleType {
  // eslint-disable-next-line
  url: string | boolean | any | undefined;
  title: string;
  breadcrumbs: unknown;
  type: string;
  link: string | undefined;
}

@Component({
  selector: 'app-breadcrumb',
  imports: [CommonModule, RouterModule, SharedModule],
  templateUrl: './breadcrumb.component.html',
  styleUrls: ['./breadcrumb.component.scss']
})
export class BreadcrumbComponent {
  private route = inject(Router);
  private titleService = inject(Title);

  // public props
  @Input() type: string;
  readonly Component = input(false);
  readonly dashboard = input(true);

  navigations: NavigationItem[];
  ComponentNavigations: NavigationItem[];
  breadcrumbList: Array<string> = [];
  navigationList: titleType[];
  componentList: titleType[];

  // constructor
  constructor() {
    this.navigations = menus;
    this.ComponentNavigations = componentMenus;
    this.type = 'theme1';
    this.setBreadcrumb();
  }

  // public method
  setBreadcrumb() {
    this.route.events.subscribe((router: Event) => {
      if (router instanceof NavigationEnd) {
        const activeLink = router.url;
        const breadcrumbList = this.filterNavigation(this.navigations, activeLink);
        this.navigationList = breadcrumbList;
        this.componentList = this.filterNavigation(this.ComponentNavigations, activeLink);
        const title = breadcrumbList[breadcrumbList.length - 1]?.title || 'Welcome';
        this.titleService.setTitle(title + ' | Able pro Angular Admin Template');
      }
    });
  }

  filterNavigation(navItems: NavigationItem[], activeLink: string): titleType[] {
    for (const navItem of navItems) {
      if (navItem.type === 'item' && 'url' in navItem && navItem.url === activeLink) {
        return [
          {
            url: 'url' in navItem ? navItem.url : false,
            title: navItem.title,
            link: navItem.link,
            breadcrumbs: 'breadcrumbs' in navItem ? navItem.breadcrumbs : true,
            type: navItem.type
          }
        ];
      }
      if ((navItem.type === 'group' || navItem.type === 'collapse') && 'children' in navItem) {
        const breadcrumbList = this.filterNavigation(navItem.children!, activeLink);
        if (breadcrumbList.length > 0) {
          breadcrumbList.unshift({
            url: 'url' in navItem ? navItem.url : false,
            title: navItem.title,
            link: navItem.link,
            breadcrumbs: 'breadcrumbs' in navItem ? navItem.breadcrumbs : true,
            type: navItem.type
          });
          return breadcrumbList;
        }
      }
    }
    return [];
  }
}
