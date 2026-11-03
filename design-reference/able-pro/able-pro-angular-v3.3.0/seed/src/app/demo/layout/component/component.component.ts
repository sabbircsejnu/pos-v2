// angular import
import { Component } from '@angular/core';

// project import
import { componentMenus } from 'src/app/demo/data/component';
import { ComponentNavigationComponent } from 'src/app/@theme/components/navigation/navigation.component';

@Component({
  selector: 'app-component',
  imports: [ComponentNavigationComponent],
  templateUrl: './component.component.html',
  styleUrls: ['./component.component.scss']
})
export class ComponentComponent {
  menus = componentMenus;
}
