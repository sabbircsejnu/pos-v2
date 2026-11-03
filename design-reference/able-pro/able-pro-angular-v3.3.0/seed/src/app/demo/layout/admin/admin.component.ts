// Angular import
import { AfterViewInit, Component, OnInit, effect, inject, viewChild } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatDrawer, MatDrawerMode } from '@angular/material/sidenav';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';

// Project import
import { AbleProConfig } from 'src/app/app-config';
import { menus } from 'src/app/demo/data/menu';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { SharedModule } from '../../shared/shared.module';
import { NavBarComponent } from 'src/app/@theme/layouts/toolbar/toolbar.component';
import { VerticalMenuComponent } from 'src/app/@theme/layouts/menu/vertical-menu';
import { HorizontalMenuComponent } from 'src/app/@theme/layouts/menu/horizontal-menu';
import { CompactMenuComponent } from 'src/app/@theme/layouts/menu/compact-menu';
import { BreadcrumbComponent } from 'src/app/@theme/components/breadcrumb/breadcrumb.component';
import { ConfigurationComponent } from 'src/app/@theme/layouts/configuration/configuration.component';
import { FooterComponent } from 'src/app/@theme/layouts/footer/footer.component';

// service
import { BuyNowLinkService } from '../../../@theme/services/buy-now-link.service';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';

// const import
import { MIN_WIDTH_1025PX, MAX_WIDTH_1024PX, VERTICAL, HORIZONTAL, COMPACT, RTL, LTR } from 'src/app/@theme/const';

// theme version
import { environment } from 'src/environments/environment';

//type
import { Navigation } from 'src/app/@theme/types/navigation';
import { Role } from 'src/app/@theme/types/role';

@Component({
  selector: 'app-admin',
  imports: [
    SharedModule,
    RouterModule,
    CommonModule,
    NavBarComponent,
    VerticalMenuComponent,
    HorizontalMenuComponent,
    CompactMenuComponent,
    BreadcrumbComponent,
    ConfigurationComponent,
    FooterComponent
  ],
  templateUrl: './admin.component.html',
  styleUrls: ['./admin.component.scss']
})
export class AdminComponent implements OnInit, AfterViewInit {
  private breakpointObserver = inject(BreakpointObserver);
  private themeService = inject(ThemeLayoutService);
  buyNowLinkService = inject(BuyNowLinkService);
  authenticationService = inject(AuthenticationService);

  // public props
  readonly sidebar = viewChild<MatDrawer>('sidebar');
  menus: Navigation[] = menus;
  modeValue: MatDrawerMode = 'side';
  direction: string = LTR;
  currentApplicationVersion = environment.appVersion;
  currentLayout: string = 'vertical';
  rtlMode: boolean = false;
  windowWidth: number = window.innerWidth;

  // Constructor
  constructor() {
    effect(() => {
      this.updateThemeLayout(this.themeService.layout());
    });
    effect(() => {
      this.themeDirection(this.themeService.directionChange());
    });
  }

  // life cycle event
  ngOnInit() {
    this.breakpointObserver.observe([MIN_WIDTH_1025PX, MAX_WIDTH_1024PX]).subscribe((result) => {
      if (result.breakpoints[MAX_WIDTH_1024PX]) {
        this.modeValue = 'over';
      } else if (result.breakpoints[MIN_WIDTH_1025PX]) {
        this.modeValue = 'side';
      }
    });

    /**
     * Dashboard menu sidebar toggle listener
     */
    this.themeService.dashBoardMenuState.subscribe(() => {
      this.sidebar()!.toggle();
    });

    /**
     * current login user role
     */
    const currentUser = this.authenticationService.currentUserValue;
    const userRoles = currentUser?.user.role ? [currentUser.user.role] : [Role.Admin];

    /**
     * Role base menu filtering
     */
    this.menus = this.RoleBaseFilterMenu(menus, userRoles);
  }

  ngAfterViewInit() {
    this.rtlMode = AbleProConfig.isRtlLayout;

    this.currentLayout = AbleProConfig.layout;

    this.manageLayout(this.currentLayout);
  }

  /**
   * Role base menu filtering
   */
  RoleBaseFilterMenu(menus: Navigation[], userRoles: string[], parentRoles: string[] = [Role.Admin]): Navigation[] {
    return menus.map((item) => {
      // If item doesn't have a specific role, inherit roles from parent
      const itemRoles = item.role ? item.role : parentRoles;

      // If item has children, recursively filter them, passing current item's roles as parentRoles
      if (item.children) {
        item.children = this.RoleBaseFilterMenu(item.children, userRoles, itemRoles);
      }

      return item; // Return the item whether it is visible or disabled
    });
  }

  /**
   * Listen to Theme direction change. RTL/LTR
   */
  private themeDirection(direction: string) {
    this.rtlMode = direction === RTL ? true : false;
    this.manageLayout(this.currentLayout);
  }

  /**
   * Listen to theme layout changes
   */
  private updateThemeLayout(layout: string) {
    this.currentLayout = layout;
    this.manageLayout(layout);
  }

  /**
   * Manage layout of theme
   */
  private manageLayout(layout: string) {
    const drawerContent = document.querySelector('.mat-drawer-content') as HTMLElement;
    if (drawerContent) {
      if (layout === VERTICAL) {
        if (this.windowWidth > 1025) {
          drawerContent.style.marginLeft = this.rtlMode === true ? '0px' : '280px';
          drawerContent.style.marginRight = this.rtlMode === true ? '280px' : '0px';
        }
        this.direction = this.rtlMode === true ? RTL : LTR;
      } else if (layout === COMPACT) {
        if (this.windowWidth > 1025) {
          drawerContent.style.marginLeft = this.rtlMode == true ? '0px' : '90px';
          drawerContent.style.marginRight = this.rtlMode == true ? '90px' : '0px';
        }
        this.direction = this.rtlMode == true ? RTL : LTR;
      } else if (layout == HORIZONTAL) {
        drawerContent.style.marginLeft = '0px';
      }
    }
  }
}
