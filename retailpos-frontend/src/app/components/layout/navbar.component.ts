import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { User } from '../../models/auth.models';
import { Outlet } from '../../models/outlet.model';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.css']
})
export class NavbarComponent implements OnInit, OnDestroy {
  private authService = inject(AuthService);
  private outletService = inject(OutletService);
  private router = inject(Router);
  menuService = inject(MenuService);

  showUserMenu = false;
  showRoleSwitchMenu = false;
  globalSearch = '';
  currentUser = signal<User | null>(null);
  outlets = signal<Outlet[]>([]);
  selectedActingRole = '';
  selectedActingOutletId: number | null = null;
  switchingRole = signal(false);
  switchError = signal<string | null>(null);

  readonly switchableRoles = ['OutletManager', 'Cashier', 'Salesman', 'Stock Manager'];

  private userSubscription?: Subscription;

  ngOnInit(): void {
    this.userSubscription = this.authService.currentUser$.subscribe(user => {
      this.currentUser.set(user);
      if (user?.isBusinessOwner && this.outlets().length === 0) {
        this.outletService.getAllOutlets().subscribe({
          next: res => this.outlets.set(res?.data ?? []),
          error: () => this.outlets.set([])
        });
      }
    });
  }

  ngOnDestroy(): void {
    this.userSubscription?.unsubscribe();
  }

  closeDropdowns(): void {
    this.showRoleSwitchMenu = false;
    this.showUserMenu = false;
  }

  toggleUserMenu(): void {
    this.showUserMenu = !this.showUserMenu;
  }

  toggleRoleSwitchMenu(event: Event): void {
    event.stopPropagation();
    this.showRoleSwitchMenu = !this.showRoleSwitchMenu;
    this.switchError.set(null);
  }

  confirmSwitchRole(event: Event): void {
    event.stopPropagation();
    if (!this.selectedActingRole) {
      this.switchError.set('Select a role.');
      return;
    }
    this.switchingRole.set(true);
    this.switchError.set(null);
    this.authService.switchRole({
      actingRole: this.selectedActingRole,
      actingOutletId: this.selectedActingOutletId ?? undefined
    }).subscribe({
      next: () => {
        this.switchingRole.set(false);
        this.showRoleSwitchMenu = false;
        this.showUserMenu = false;
        this.router.navigate(['/dashboard']);
      },
      error: err => {
        this.switchingRole.set(false);
        this.switchError.set(err?.error?.message ?? 'Unable to switch role.');
      }
    });
  }

  returnOwnerMode(event: Event): void {
    event.stopPropagation();
    this.switchingRole.set(true);
    this.authService.returnOwnerMode().subscribe({
      next: () => {
        this.switchingRole.set(false);
        this.showRoleSwitchMenu = false;
        this.showUserMenu = false;
        this.selectedActingRole = '';
        this.selectedActingOutletId = null;
        this.router.navigate(['/dashboard']);
      },
      error: err => {
        this.switchingRole.set(false);
        this.switchError.set(err?.error?.message ?? 'Unable to return to owner mode.');
      }
    });
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }

  toggleSidebar(): void {
    this.menuService.toggleSidebar();
  }

  runGlobalSearch(event?: Event): void {
    event?.stopPropagation();
    const query = this.globalSearch.trim();
    if (!query) {
      return;
    }

    this.router.navigate(['/search'], {
      queryParams: { q: query }
    });
  }
}
