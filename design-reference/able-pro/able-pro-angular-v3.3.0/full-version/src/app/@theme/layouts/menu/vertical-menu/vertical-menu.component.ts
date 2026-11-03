// Angular import
import { Component, effect, inject, input } from '@angular/core';
import { Location, LocationStrategy } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { NavigationItem } from 'src/app/@theme/types/navigation';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { HORIZONTAL, VERTICAL, COMPACT } from 'src/app/@theme/const';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { MenuGroupVerticalComponent } from './menu-group/menu-group.component';
import { MenuItemVerticalComponent } from './menu-item/menu-item.component';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';
import { MenuCollapseComponent } from './menu-collapse/menu-collapse.component';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

@Component({
  selector: 'app-vertical-menu',
  imports: [SharedModule, MenuGroupVerticalComponent, MenuItemVerticalComponent, MenuCollapseComponent, RouterModule, NgxScrollbar],
  templateUrl: './vertical-menu.component.html',
  styleUrls: ['./vertical-menu.component.scss']
})
export class VerticalMenuComponent {
  private location = inject(Location);
  private locationStrategy = inject(LocationStrategy);
  private themeService = inject(ThemeLayoutService);
  authenticationService = inject(AuthenticationService);

  // public props
  readonly menus = input<NavigationItem[]>();
  showUser: false;
  showContent = true;

  // Constructor
  constructor() {
    effect(() => {
      this.updateThemeLayout(this.themeService.layout());
    });
  }

  // public method
  fireOutClick() {
    let current_url = this.location.path();
    const baseHref = this.locationStrategy.getBaseHref();
    if (baseHref) {
      current_url = baseHref + this.location.path();
    }
    const link = "a.nav-link[ href='" + current_url + "' ]";
    const ele = document.querySelector(link);
    if (ele !== null && ele !== undefined) {
      const parent = ele.parentElement;
      const up_parent = parent?.parentElement?.parentElement;
      const last_parent = up_parent?.parentElement;
      if (parent?.classList.contains('coded-hasmenu')) {
        parent.classList.add('coded-trigger');
        parent.classList.add('active');
      } else if (up_parent?.classList.contains('coded-hasmenu')) {
        up_parent.classList.add('coded-trigger');
        up_parent.classList.add('active');
      } else if (last_parent?.classList.contains('coded-hasmenu')) {
        last_parent.classList.add('coded-trigger');
        last_parent.classList.add('active');
      }
    }
  }

  private updateThemeLayout(layout: string) {
    if (layout == VERTICAL) {
      this.showContent = true;
    }
    if (layout == HORIZONTAL) {
      this.showContent = false;
    }
    if (layout == COMPACT) {
      this.showContent = false;
    }
  }

  // user Logout
  logout() {
    this.authenticationService.logout();
  }
}
