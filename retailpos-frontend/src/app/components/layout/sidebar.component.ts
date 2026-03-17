import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MenuService } from '../../services/menu.service';
import { MenuItem } from '../../models/menu.model';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent {
  menuService = inject(MenuService);
  private router = inject(Router);

  get childMenuItems(): MenuItem[] {
    return this.menuService.getChildMenuItems();
  }

  get activeParentMenu(): MenuItem | null {
    return this.menuService.activeParentMenu();
  }

  navigateToChild(child: MenuItem): void {
    if (child.route) {
      this.router.navigate([child.route]);
    }
  }
}
