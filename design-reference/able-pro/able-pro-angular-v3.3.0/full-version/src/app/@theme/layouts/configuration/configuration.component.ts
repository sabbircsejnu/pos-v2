// angular import
import { Component, OnInit, Renderer2, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AbleProConfig } from 'src/app/app-config';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';
import { HORIZONTAL, VERTICAL, COMPACT, RTL, LTR, LIGHT, DARK } from '../../const';

@Component({
  selector: 'app-configuration',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './configuration.component.html',
  styleUrls: ['./configuration.component.scss']
})
export class ConfigurationComponent implements OnInit {
  private renderer = inject(Renderer2);
  private themeService = inject(ThemeLayoutService);

  // public props
  styleSelectorToggle = signal<boolean>(false); // open configuration menu
  layoutType!: string; // layouts type
  layout!: string; // vertical, horizontal, compact
  bodyColor!: string; // theme color
  contrast!: boolean; // theme contrast
  caption!: boolean; // menu title hide
  rtlLayout!: boolean; // rtl theme
  boxLayouts!: boolean; // content is box-container
  resetLayoutType!: string; // reset layouts

  // life cycle event
  ngOnInit() {
    this.layout = AbleProConfig.layout;
    this.setMenuOrientation(this.layout);
    this.layoutType = AbleProConfig.isDarkMode;
    this.SetLayouts(this.layoutType);
    this.bodyColor = AbleProConfig.theme_color;
    this.SetBodyColor(this.bodyColor);
    this.contrast = AbleProConfig.theme_contrast;
    this.setThemeContrast(this.contrast);
    this.caption = AbleProConfig.menu_caption;
    this.setMenuCaption(this.caption);
    this.rtlLayout = AbleProConfig.isRtlLayout;
    this.setRtlLayout(this.rtlLayout);
    this.boxLayouts = AbleProConfig.isBox_container;
    this.setBoxLayouts(this.boxLayouts);
  }

  // public method

  // Reset Layouts
  setResetLayout(layout: string) {
    if (layout === 'reset') {
      this.ngOnInit();
    }
  }

  // customs layouts
  removeAllLayouts() {
    const layouts = [LIGHT, DARK];
    layouts.forEach((layout) => {
      this.renderer.removeClass(document.body, layout);
      document.querySelector('.auto-button')?.classList.remove('active');
    });
  }

  SetLayouts(layout: string) {
    this.layoutType = layout;
    this.removeAllLayouts();
    this.renderer.addClass(document.body, layout);
    this.themeService.isDarkMode.set(layout);
  }

  layout_change_default(layout: string) {
    if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
      layout = DARK;
    } else {
      layout = LIGHT;
    }
    this.SetLayouts(layout);

    document.querySelector('.auto-button')?.classList.add('active');
  }

  // customs theme
  removeAllThemes() {
    const themes = [
      'blue-theme',
      'indigo-theme',
      'purple-theme',
      'pink-theme',
      'red-theme',
      'orange-theme',
      'yellow-theme',
      'green-theme',
      'teal-theme',
      'cyan-theme'
    ];
    themes.forEach((theme) => {
      this.renderer.removeClass(document.body, theme);
    });
  }

  SetBodyColor(theme: string) {
    this.bodyColor = theme;
    this.removeAllThemes();
    this.renderer.addClass(document.body, theme);
    this.themeService.color.set(theme);
  }

  // theme body menu and header background color change
  setThemeContrast(contrast: boolean) {
    if (contrast) {
      this.renderer.addClass(document.body, 'theme-contrast');
      this.contrast = true;
    } else {
      this.renderer.removeClass(document.body, 'theme-contrast');
      this.contrast = false;
    }
  }

  // menu title hide and show
  setMenuCaption(caption: boolean) {
    if (caption) {
      document.querySelector('.pc-sidebar')?.classList.add('caption-hide');
      this.caption = true;
    } else {
      document.querySelector('.pc-sidebar')?.classList.remove('caption-hide');
      this.caption = false;
    }
  }

  // box container layouts
  setBoxLayouts(boxLayouts: boolean) {
    if (boxLayouts) {
      document.querySelector('.app-container')?.classList.add('container');
      this.boxLayouts = true;
    } else {
      document.querySelector('.app-container')?.classList.remove('container');
      this.boxLayouts = false;
    }
  }

  // rtl theme layout
  setRtlLayout(rtl: boolean) {
    if (rtl) {
      this.renderer.addClass(document.body, 'able-pro-rtl');
      this.rtlLayout = true;
      this.themeService.directionChange.set(RTL);
    } else {
      this.renderer.removeClass(document.body, 'able-pro-rtl');
      this.rtlLayout = false;
      this.themeService.directionChange.set(LTR);
    }
  }

  setMenuOrientation(layout: string) {
    this.layout = layout;
    document.querySelector('.pc-sidebar')?.classList.remove(HORIZONTAL);
    document.querySelector('.pc-sidebar')?.classList.remove(COMPACT);
    document.querySelector('.pc-sidebar')?.classList.remove(VERTICAL);
    document.querySelector('.pc-sidebar')?.classList.add(layout);
    this.themeService.layout.set(layout);
  }
}
