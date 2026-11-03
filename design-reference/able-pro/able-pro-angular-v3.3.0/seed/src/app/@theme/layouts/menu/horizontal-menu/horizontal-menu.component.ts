// Angular import
import { AfterViewInit, Component, ElementRef, NgZone, inject, input, viewChild } from '@angular/core';
import { CommonModule, Location, LocationStrategy } from '@angular/common';

// project import
import { NavigationItem } from 'src/app/@theme/types/navigation';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { MenuGroupHorizontalComponent } from './menu-group/menu-group.component';
import { MenuCollapseComponent } from './menu-collapse/menu-collapse.component';
import { MenuItemHorizontalComponent } from './menu-item/menu-item.component';

@Component({
  selector: 'app-horizontal-menu',
  imports: [SharedModule, CommonModule, MenuGroupHorizontalComponent, MenuCollapseComponent, MenuItemHorizontalComponent],
  templateUrl: './horizontal-menu.component.html',
  styleUrls: ['./horizontal-menu.component.scss']
})
export class HorizontalMenuComponent implements AfterViewInit {
  private zone = inject(NgZone);
  private location = inject(Location);
  private locationStrategy = inject(LocationStrategy);

  /**
   * List of menu items to be rendered
   */
  readonly menus = input<NavigationItem[]>();

  /**
   * Theme Configuration
   */
  readonly config = input<object>();
  readonly themeLayout = input<string>();
  prevDisabled: string;
  nextDisabled: string;
  contentWidth: number;
  wrapperWidth: number;
  scrollWidth: number;
  windowWidth: number;

  readonly navbarContent = viewChild.required<ElementRef>('navbarContent');
  readonly navbarWrapper = viewChild.required<ElementRef>('navbarWrapper');

  // Constructor
  constructor() {
    this.windowWidth = window.innerWidth;
    this.prevDisabled = 'disabled';
    this.nextDisabled = '';
    this.scrollWidth = 0;
    this.contentWidth = 0;
  }

  // public method
  ngAfterViewInit() {
    this.contentWidth = this.navbarContent().nativeElement.clientWidth;
    this.wrapperWidth = this.navbarWrapper().nativeElement.clientWidth;
  }

  // Horizontal Menu
  scrollPlus() {
    this.scrollWidth = this.scrollWidth + (this.wrapperWidth - 200);
    if (this.scrollWidth > this.contentWidth - this.wrapperWidth) {
      this.scrollWidth = this.contentWidth - this.wrapperWidth + 200;
      this.nextDisabled = 'disabled';
    }
    this.prevDisabled = '';
    (document.querySelector('#side-nav-horizontal') as HTMLElement).style.marginLeft = '-' + this.scrollWidth + 'px';
  }

  scrollMinus() {
    this.scrollWidth = this.scrollWidth - this.wrapperWidth;
    if (this.scrollWidth < 0) {
      this.scrollWidth = 0;
      this.prevDisabled = 'disabled';
    }
    this.nextDisabled = '';
    (document.querySelector('#side-nav-horizontal') as HTMLElement).style.marginLeft = '-' + this.scrollWidth + 'px';
  }

  fireLeave() {
    const sections = document.querySelectorAll('.coded-hasmenu');
    for (let i = 0; i < sections.length; i++) {
      sections[i].classList.remove('active');
      sections[i].classList.remove('coded-trigger');
    }

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
        parent.classList.add('active');
      } else if (up_parent?.classList.contains('coded-hasmenu')) {
        up_parent.classList.add('active');
      } else if (last_parent?.classList.contains('coded-hasmenu')) {
        last_parent.classList.add('active');
      }
    }
  }
}
