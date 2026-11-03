// Angular import
import { Component, OnInit, inject, input } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule, Location } from '@angular/common';

// project import
import { NavigationItem } from 'src/app/@theme/types/navigation';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { MenuItemVerticalComponent } from '../menu-item/menu-item.component';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';

//type
import { Role } from 'src/app/@theme/types/role';

@Component({
  selector: 'app-menu-collapse',
  imports: [SharedModule, RouterModule, MenuItemVerticalComponent, CommonModule],
  templateUrl: './menu-collapse.component.html',
  styleUrls: ['./menu-collapse.component.scss']
})
export class MenuCollapseComponent implements OnInit {
  private location = inject(Location);
  private authenticationService = inject(AuthenticationService);

  // public props
  current_url: string = ''; // Add current URL property
  isEnabled: boolean = false;

  // all Version Get Item(Component Name Take)
  readonly item = input<NavigationItem>();
  readonly parentRole = input<string[]>();

  visible = false;
  windowWidth = window.innerWidth;

  // public method
  ngOnInit() {
    this.current_url = this.location.path();
    //eslint-disable-next-line
    //@ts-ignore
    const baseHref = this.location['_baseHref'] || ''; // Use baseHref if necessary
    this.current_url = baseHref + this.current_url;

    // Timeout to allow DOM to fully render before checking for the links
    setTimeout(() => {
      const links = document.querySelectorAll('a.nav-link') as NodeListOf<HTMLAnchorElement>;
      links.forEach((link: HTMLAnchorElement) => {
        if (link.getAttribute('href') === this.current_url) {
          let parent = link.parentElement;
          while (parent && parent.classList) {
            if (parent.classList.contains('coded-hasmenu')) {
              parent.classList.add('coded-trigger');
              parent.classList.add('active');
            }
            parent = parent.parentElement;
          }
        }
      });
    }, 0);

    /**
     * current login user role
     */
    const currentUserRole = this.authenticationService.currentUserValue?.user.role || Role.Admin;

    /**
     * items parent role
     */
    const parentRoleValue = this.parentRole();

    if (this.item()!.role && this.item()!.role!.length > 0) {
      if (currentUserRole) {
        const parentRole = this.parentRole();
        const allowedFromParent =
          this.item()!.isMainParent || (parentRole && parentRole.length > 0 && parentRole.includes(currentUserRole));
        if (allowedFromParent) {
          this.isEnabled = this.item()!.role!.includes(currentUserRole);
        }
      }
    } else if (parentRoleValue && parentRoleValue.length > 0) {
      // If item.role is empty, check parentRole
      if (currentUserRole) {
        this.isEnabled = parentRoleValue.includes(currentUserRole);
      }
    }
  }

  // Method to handle the collapse of the navigation menu
  navCollapse(e: MouseEvent) {
    let parent = e.target as HTMLElement;

    if (parent?.tagName === 'SPAN') {
      parent = parent.parentElement!;
    }

    parent = (parent as HTMLElement).parentElement!;

    const sections = document.querySelectorAll('.coded-hasmenu');
    for (let i = 0; i < sections.length; i++) {
      if (sections[i] !== parent) {
        sections[i].classList.remove('coded-trigger');
      }
    }

    let first_parent = parent.parentElement!;
    let pre_parent = ((parent as HTMLElement).parentElement as HTMLElement).parentElement!;
    if (first_parent.classList.contains('coded-hasmenu')) {
      do {
        first_parent.classList.add('coded-trigger');
        first_parent = (first_parent.parentElement as HTMLElement).parentElement!;
      } while (first_parent.classList.contains('coded-hasmenu'));
    } else if (pre_parent.classList.contains('coded-submenu')) {
      do {
        pre_parent.parentElement?.classList.add('coded-trigger');
        pre_parent = (((pre_parent as HTMLElement).parentElement as HTMLElement).parentElement as HTMLElement).parentElement!;
      } while (pre_parent.classList.contains('coded-submenu'));
    }
    parent.classList.toggle('coded-trigger');
  }
}
