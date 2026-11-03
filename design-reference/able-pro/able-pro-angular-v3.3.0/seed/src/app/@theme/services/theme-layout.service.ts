import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class ThemeLayoutService {
  // theme menu sidebar show and hide
  dashBoardMenuState = new Subject();

  // theme component page menu sidebar show and hide
  componentMenuState = new Subject();

  // theme sidebar direction change in rtl mode
  directionChange = signal<string>('');

  // color change
  color = signal<string>('');

  // layout change
  layout = signal<string>('');

  // themeMode Change
  isDarkMode = signal<string>('');

  drawerOpen = signal(false);
  componentDrawerOpen = signal(false);

  /**
   * Toggle Dashboard vertical menu
   */
  toggleSideDrawer() {
    this.dashBoardMenuState.next(!this.drawerOpen());
  }

  /**
   * Toggle Component vertical menu
   */
  toggleMenuSide() {
    this.componentMenuState.next(!this.componentDrawerOpen());
  }
}
