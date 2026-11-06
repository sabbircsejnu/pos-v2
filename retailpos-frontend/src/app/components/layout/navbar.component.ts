import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { MenuService } from '../../services/menu.service';
import { ActingLocationType, User, UserContextOption, UserContextOptions } from '../../models/auth.models';
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
  private router = inject(Router);
  menuService = inject(MenuService);

  showUserMenu = false;
  showRoleSwitchMenu = false;
  globalSearch = '';
  currentUser = signal<User | null>(null);
  switchOptions = signal<UserContextOptions | null>(null);
  selectedActingRole = '';
  selectedLocationType: ActingLocationType = 'outlet';
  selectedLocationId: number | null = null;
  switchingRole = signal(false);
  switchError = signal<string | null>(null);

  private userSubscription?: Subscription;
  private optionsSubscription?: Subscription;
  private optionsUserId: number | null = null;

  ngOnInit(): void {
    this.userSubscription = this.authService.currentUser$.subscribe(user => {
      this.currentUser.set(user);
      if ((user?.id ?? null) !== this.optionsUserId) {
        this.optionsUserId = user?.id ?? null;
        this.loadSwitchOptions();
      }
    });
  }

  /** A switched session must always be able to leave, even after its access was removed. */
  showSwitcher(): boolean {
    return this.switchOptions()?.canSwitch === true || this.currentUser()?.isRoleSwitched === true;
  }

  locationChoices(): UserContextOption[] {
    const options = this.switchOptions();
    if (!options) {
      return [];
    }
    return this.selectedLocationType === 'warehouse' ? options.warehouses : options.outlets;
  }

  onLocationTypeChange(): void {
    this.selectedLocationId = null;
    this.selectSingleLocation();
  }

  canConfirmSwitch(): boolean {
    return !!this.selectedActingRole && (this.selectedLocationType === 'all' || this.selectedLocationId !== null);
  }

  actingLocationLabel(): string {
    const user = this.currentUser();
    return user?.actingOutletName ?? user?.actingWarehouseName ?? 'All Locations';
  }

  private loadSwitchOptions(): void {
    this.optionsSubscription?.unsubscribe();
    if (this.optionsUserId === null) {
      this.switchOptions.set(null);
      return;
    }

    this.optionsSubscription = this.authService.getSwitchOptions().subscribe({
      next: options => {
        this.switchOptions.set(options);
        this.applyOptionDefaults(options);
      },
      error: () => this.switchOptions.set(null)
    });
  }

  private applyOptionDefaults(options: UserContextOptions): void {
    if (!options.roles.some(role => role.name === this.selectedActingRole)) {
      this.selectedActingRole = options.roles.length === 1 ? options.roles[0].name : '';
    }

    const typeStillOffered = (this.selectedLocationType === 'all' && options.allowAllLocations)
      || (this.selectedLocationType === 'outlet' && options.outlets.length > 0)
      || (this.selectedLocationType === 'warehouse' && options.warehouses.length > 0);
    if (!typeStillOffered) {
      this.selectedLocationType = options.outlets.length > 0
        ? 'outlet'
        : options.warehouses.length > 0 ? 'warehouse' : 'all';
    }

    if (!this.locationChoices().some(location => location.id === this.selectedLocationId)) {
      this.selectedLocationId = null;
    }
    this.selectSingleLocation();
  }

  private selectSingleLocation(): void {
    const choices = this.locationChoices();
    if (this.selectedLocationType !== 'all' && this.selectedLocationId === null && choices.length === 1) {
      this.selectedLocationId = choices[0].id;
    }
  }

  ngOnDestroy(): void {
    this.userSubscription?.unsubscribe();
    this.optionsSubscription?.unsubscribe();
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
    if (this.showRoleSwitchMenu) {
      // Assignments may have changed since the options were last loaded.
      this.loadSwitchOptions();
    }
  }

  confirmSwitchRole(event: Event): void {
    event.stopPropagation();
    if (!this.canConfirmSwitch()) {
      this.switchError.set('Select a role and location.');
      return;
    }
    this.switchingRole.set(true);
    this.switchError.set(null);
    this.authService.switchRole({
      actingRole: this.selectedActingRole,
      actingLocationType: this.selectedLocationType,
      actingLocationId: this.selectedLocationType === 'all' ? undefined : this.selectedLocationId ?? undefined
    }).subscribe({
      next: () => {
        this.switchingRole.set(false);
        this.showRoleSwitchMenu = false;
        this.showUserMenu = false;
        this.router.navigate(['/dashboard']);
      },
      error: err => {
        this.switchingRole.set(false);
        this.switchError.set(
          err?.error?.message
            ?? (err?.status === 403 ? 'You are not allowed to switch to this context.' : 'Unable to switch context.')
        );
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
        this.selectedLocationId = null;
        this.loadSwitchOptions();
        this.router.navigate(['/dashboard']);
      },
      error: err => {
        this.switchingRole.set(false);
        this.switchError.set(err?.error?.message ?? 'Unable to return to your own context.');
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
