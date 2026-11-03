// Angular import
import { Component, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// Project import
import { NavigationItem } from 'src/app/@theme/types/navigation';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-component-item',
  imports: [SharedModule, CommonModule, RouterModule],
  templateUrl: './item.component.html',
  styleUrls: ['./item.component.scss']
})
export class ComponentItemComponent {
  private themeService = inject(ThemeLayoutService);

  // public props
  readonly item = input.required<NavigationItem>();

  // public method
  toggleMenu(event: MouseEvent) {
    if (window.innerWidth < 1025) {
      this.themeService.toggleMenuSide();
    }
    const ele = event.target as HTMLElement;
    if (ele !== null && ele !== undefined) {
      const parent = ele.parentElement as HTMLElement;
      const up_parent = ((parent.parentElement as HTMLElement).parentElement as HTMLElement).parentElement as HTMLElement;
      const last_parent = up_parent.parentElement;
      const sections = document.querySelectorAll('.coded-hasmenu');
      for (let i = 0; i < sections.length; i++) {
        sections[i].classList.remove('active');
        sections[i].classList.remove('coded-trigger');
      }
      if (parent.classList.contains('coded-hasmenu')) {
        parent.classList.add('coded-trigger');
        parent.classList.add('active');
      } else if (up_parent.classList.contains('coded-hasmenu')) {
        up_parent.classList.add('coded-trigger');
        up_parent.classList.add('active');
      } else if (last_parent?.classList.contains('coded-hasmenu')) {
        last_parent.classList.add('coded-trigger');
        last_parent.classList.add('active');
      }
    }
  }
}
