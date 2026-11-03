// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { DASHBOARD_PATH } from 'src/app/app-config';

@Component({
  selector: 'app-under-constructor',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './under-constructor.component.html',
  styleUrls: ['../maintenance.scss', './under-constructor.component.scss']
})
export class UnderConstructorComponent {
  default_path = DASHBOARD_PATH;
}
