// angular import
import { Component, OnInit, inject, input, viewChild } from '@angular/core';
import { RouterModule } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';

// project import
import { NavigationItem } from '../../types/navigation';
import { MatDrawer, MatDrawerMode } from '@angular/material/sidenav';
import { ThemeLayoutService } from '../../services/theme-layout.service';
import { MIN_WIDTH_1025PX, MAX_WIDTH_1024PX } from 'src/app/@theme/const';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { BreadcrumbComponent } from '../breadcrumb/breadcrumb.component';
import { ComponentGroupComponent } from './group/group.component';
import { SearchFilterPipe } from '../../services/search-filter.pipe';
import { NgxScrollbar } from '../ngx-scrollbar/ngx-scrollbar';

@Component({
  selector: 'app-component-navigation',
  imports: [SharedModule, BreadcrumbComponent, RouterModule, ComponentGroupComponent, SearchFilterPipe, NgxScrollbar],
  templateUrl: './navigation.component.html',
  styleUrls: ['./navigation.component.scss']
})
export class ComponentNavigationComponent implements OnInit {
  private breakpointObserver = inject(BreakpointObserver);
  private themeService = inject(ThemeLayoutService);

  // public props
  readonly menus = input<NavigationItem[]>();
  readonly menuSide = viewChild<MatDrawer>('menuSide');

  windowWidth = window.innerWidth;
  modeValue: MatDrawerMode = 'side';
  searchMenus: string;

  // life cycle event
  ngOnInit() {
    this.breakpointObserver.observe([MIN_WIDTH_1025PX, MAX_WIDTH_1024PX]).subscribe((result) => {
      if (result.breakpoints['(max-width: 1024.98px)']) {
        this.modeValue = 'over';
        (document.querySelector('#nav-ps-able-pro') as HTMLElement).style.height = 'calc(100vh - 163px)';
      } else if (result.breakpoints[MIN_WIDTH_1025PX]) {
        this.modeValue = 'side';
        this.menuSide()?.open();
      }
    });

    this.themeService.componentMenuState.subscribe(() => {
      this.menuSide()!.toggle();
    });
  }

  // public method
  toggleMenu() {
    this.themeService.toggleMenuSide();
  }
}
