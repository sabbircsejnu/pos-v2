// angular import
import { Component, effect, inject, OnInit } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';

// project import
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { SharedModule } from 'src/app/demo/shared/shared.module';

// const import
import { MIN_WIDTH_768PX, MAX_WIDTH_767PX, VERTICAL, HORIZONTAL, COMPACT } from 'src/app/@theme/const';

@Component({
  selector: 'app-nav-left',
  imports: [SharedModule],
  templateUrl: './toolbar-left.component.html',
  styleUrls: ['./toolbar-left.component.scss']
})
export class NavLeftComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);
  private breakpointObserver = inject(BreakpointObserver);

  mobileMedia: boolean;

  // public props
  showToggleMenu: boolean = true;

  // constructor
  constructor() {
    effect(() => {
      this.updateThemeLayout(this.themeService.layout());
    });
  }

  ngOnInit() {
    this.breakpointObserver.observe([MIN_WIDTH_768PX, MAX_WIDTH_767PX]).subscribe((result) => {
      if (result.breakpoints[MAX_WIDTH_767PX]) {
        this.mobileMedia = true;
      } else if (result.breakpoints[MIN_WIDTH_768PX]) {
        this.mobileMedia = false;
      }
    });
  }

  private updateThemeLayout(layout: string) {
    if (layout === VERTICAL) {
      this.showToggleMenu = true;
    }
    if (layout == HORIZONTAL) {
      this.showToggleMenu = false;
    }
    if (layout === COMPACT) {
      this.showToggleMenu = true;
    }
  }

  // public method
  toggleMenu() {
    this.themeService.toggleSideDrawer();
  }
}
