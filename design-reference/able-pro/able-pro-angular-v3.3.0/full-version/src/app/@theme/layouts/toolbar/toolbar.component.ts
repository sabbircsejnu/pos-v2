// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { NavRightComponent } from './toolbar-right/toolbar-right.component';
import { NavLeftComponent } from './toolbar-left/toolbar-left.component';

@Component({
  selector: 'app-nav-bar',
  imports: [SharedModule, NavLeftComponent, NavRightComponent, CommonModule],
  templateUrl: './toolbar.component.html',
  styleUrls: ['./toolbar.component.scss']
})
export class NavBarComponent {
  // public props
  HeaderBlur: boolean;
}
