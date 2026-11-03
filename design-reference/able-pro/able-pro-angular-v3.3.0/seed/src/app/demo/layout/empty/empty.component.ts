// angular import
import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

// project import
import { ConfigurationComponent } from 'src/app/@theme/layouts/configuration/configuration.component';

@Component({
  selector: 'app-empty',
  imports: [ConfigurationComponent, RouterModule],
  templateUrl: './empty.component.html',
  styleUrls: ['./empty.component.scss']
})
export class EmptyComponent {}
