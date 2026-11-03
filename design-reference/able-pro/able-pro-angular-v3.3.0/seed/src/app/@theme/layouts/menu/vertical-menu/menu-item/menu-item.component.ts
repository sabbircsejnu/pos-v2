// Angular import
import { Component, OnInit, inject, input } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';

// Project import
import { NavigationItem } from 'src/app/@theme/types/navigation';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';
import { Role } from 'src/app/@theme/types/role';

@Component({
  selector: 'app-menu-item',
  imports: [RouterModule, SharedModule, CommonModule],
  templateUrl: './menu-item.component.html',
  styleUrls: ['./menu-item.component.scss']
})
export class MenuItemVerticalComponent implements OnInit {
  private themeService = inject(ThemeLayoutService);
  private authenticationService = inject(AuthenticationService);

  // public props
  readonly item = input.required<NavigationItem>();
  readonly parentRole = input<string[]>();

  isEnabled: boolean = false;

  //life cycle hook
  ngOnInit() {
    /**
     * current login user role
     */
    const CurrentUserRole = this.authenticationService.currentUserValue?.user.role || Role.Admin;

    /**
     * menu items
     */
    const item = this.item();

    /**
     * items parent role
     */
    const parentRoleValue = this.parentRole();

    if (item.role && item.role.length > 0) {
      if (CurrentUserRole) {
        const parentRole = this.parentRole();
        const allowedFromParent = item.isMainParent || (parentRole && parentRole.length > 0 && parentRole.includes(CurrentUserRole));
        if (allowedFromParent) {
          this.isEnabled = item.role.includes(CurrentUserRole);
        }
      }
    } else if (parentRoleValue && parentRoleValue.length > 0) {
      // If item.role is empty, check parentRole
      if (CurrentUserRole) {
        this.isEnabled = parentRoleValue.includes(CurrentUserRole);
      }
    }
  }

  // public method
  toggleMenu(event: MouseEvent) {
    if (window.innerWidth < 1025) {
      this.themeService.toggleSideDrawer();
    }

    const ele = event.target as HTMLElement;
    if (ele !== null && ele !== undefined) {
      const parent = ele.parentElement as HTMLElement;
      const up_parent = ((parent.parentElement as HTMLElement).parentElement as HTMLElement).parentElement as HTMLElement;
      const last_parent = (up_parent.parentElement as HTMLElement).parentElement as HTMLElement;
      if (last_parent.classList.contains('coded-submenu')) {
        up_parent.classList.remove('coded-trigger');
        up_parent.classList.remove('active');
      } else {
        const sections = document.querySelectorAll('.coded-hasmenu');
        for (let i = 0; i < sections.length; i++) {
          sections[i].classList.remove('active');
          sections[i].classList.remove('coded-trigger');
        }
      }

      if (parent.classList.contains('coded-hasmenu')) {
        parent.classList.add('coded-trigger');
        parent.classList.add('active');
      } else if (up_parent.classList.contains('coded-hasmenu')) {
        up_parent.classList.add('coded-trigger');
        up_parent.classList.add('active');
      } else if (last_parent.classList.contains('coded-hasmenu')) {
        last_parent.classList.add('coded-trigger');
        last_parent.classList.add('active');
      }
    }
  }
}
