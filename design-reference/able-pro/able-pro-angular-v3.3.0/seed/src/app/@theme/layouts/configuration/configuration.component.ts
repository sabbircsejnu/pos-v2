// angular import
import { AfterViewInit, Component, inject, Renderer2 } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AbleProConfig } from 'src/app/app-config';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { HORIZONTAL, VERTICAL, COMPACT, RTL, LTR, LIGHT, DARK } from '../../const';

@Component({
  selector: 'app-configuration',
  imports: [CommonModule, SharedModule],
  templateUrl: './configuration.component.html',
  styleUrls: ['./configuration.component.scss']
})
export class ConfigurationComponent implements AfterViewInit {
  private renderer = inject(Renderer2);
  private themeService = inject(ThemeLayoutService);

  // public props
  rtlLayout!: boolean;
  bodyColor!: string;
  darkLayout!: string;
  boxLayout!: boolean;
  layout!: string;

  // life cycle event
  ngAfterViewInit(): void {
    // Apply RTL layout
    if (AbleProConfig.isRtlLayout) {
      this.renderer.addClass(document.body, 'able-pro-rtl');
      this.themeService.directionChange.set(RTL);
    } else {
      this.renderer.removeClass(document.body, 'able-pro-rtl');
      this.themeService.directionChange.set(LTR);
    }

    // Apply dark mode
    this.darkLayout = AbleProConfig.isDarkMode;
    if (this.darkLayout === 'auto') {
      if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
        this.darkLayout = DARK;
      } else {
        this.darkLayout = LIGHT;
      }
      this.renderer.addClass(document.body, this.darkLayout);
    } else {
      this.renderer.addClass(document.body, this.darkLayout);
      this.themeService.isDarkMode.set(this.darkLayout);
    }

    // Apply box layout
    if (AbleProConfig.isBox_container) {
      document.querySelector('.app-container')?.classList.add('container');
    } else {
      document.querySelector('.app-container')?.classList.remove('container');
    }

    // Apply background color
    if (AbleProConfig.theme_contrast) {
      this.renderer.addClass(document.body, 'theme-contrast');
    }

    // Apply caption hide
    if (AbleProConfig.menu_caption) {
      document.querySelector('.pc-sidebar')?.classList.add('caption-hide');
    }

    // Apply body color
    if ((this.bodyColor = AbleProConfig.theme_color)) {
      this.renderer.addClass(document.body, this.bodyColor);
    }

    // Apply layout orientation
    if (AbleProConfig.layout) {
      document.querySelector('.pc-sidebar')?.classList.remove(HORIZONTAL, VERTICAL, COMPACT);
      document.querySelector('.pc-sidebar')?.classList.add(AbleProConfig.layout);
      this.themeService.layout.set(AbleProConfig.layout);
    }
  }
}
