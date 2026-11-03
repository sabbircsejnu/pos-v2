// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { DASHBOARD_PATH } from 'src/app/app-config';

@Component({
  selector: 'app-error-401',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './error-401.component.html',
  styleUrls: ['../maintenance.scss', './error-401.component.scss']
})
export class Error401Component {
  default_path = DASHBOARD_PATH;
}
