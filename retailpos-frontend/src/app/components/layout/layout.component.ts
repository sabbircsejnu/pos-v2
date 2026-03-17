import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, NavigationEnd } from '@angular/router';
import { NavbarComponent } from './navbar.component';
import { SidebarComponent } from './sidebar.component';
import { MenuService } from '../../services/menu.service';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, NavbarComponent, SidebarComponent],
  templateUrl: './layout.component.html',
  styleUrls: ['./layout.component.css']
})
export class LayoutComponent implements OnInit {
  private router = inject(Router);
  private menuService = inject(MenuService);

  ngOnInit(): void {
    // Set active parent menu based on current route on init
    this.menuService.setActiveParentMenuByRoute(this.router.url);

    // Listen to route changes and update active parent menu
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      this.menuService.setActiveParentMenuByRoute(event.urlAfterRedirects || event.url);
    });
  }
}
