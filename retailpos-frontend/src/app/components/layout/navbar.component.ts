import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { MenuService } from '../../services/menu.service';
import { MenuItem } from '../../models/menu.model';
import { User } from '../../models/auth.models';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.css']
})
export class NavbarComponent implements OnInit, OnDestroy {
  private authService = inject(AuthService);
  private router = inject(Router);
  menuService = inject(MenuService);

  showUserMenu = false;
  openDropdown: string | null = null;
  currentUser = signal<User | null>(null);
  private userSubscription?: Subscription;

  ngOnInit(): void {
    this.userSubscription = this.authService.currentUser$.subscribe(user => {
      this.currentUser.set(user);
    });
  }

  ngOnDestroy(): void {
    this.userSubscription?.unsubscribe();
  }

  toggleDropdown(menuId: string, event: Event): void {
    event.stopPropagation();
    this.openDropdown = this.openDropdown === menuId ? null : menuId;
  }

  selectParentMenu(menu: MenuItem, event: Event): void {
    event.stopPropagation();
    
    if (menu.children && menu.children.length > 0) {
      // Set active parent and show sidebar
      this.menuService.setActiveParentMenu(menu);
      this.openDropdown = null;
      
      // Navigate to first child if parent has no route
      if (!menu.route && menu.children[0]?.route) {
        this.router.navigate([menu.children[0].route]);
      }
    } else if (menu.route) {
      // Navigate directly if no children
      this.menuService.setActiveParentMenu(null);
      this.router.navigate([menu.route]);
      this.openDropdown = null;
    }
  }

  selectChildMenu(parentMenu: MenuItem, event: Event): void {
    event.stopPropagation();
    // Set the parent menu as active to show sidebar with children
    this.menuService.setActiveParentMenu(parentMenu);
    this.openDropdown = null;
  }

  closeDropdowns(): void {
    this.openDropdown = null;
  }

  toggleUserMenu(): void {
    this.showUserMenu = !this.showUserMenu;
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }

  toggleSidebar(): void {
    this.menuService.toggleSidebar();
  }
}
