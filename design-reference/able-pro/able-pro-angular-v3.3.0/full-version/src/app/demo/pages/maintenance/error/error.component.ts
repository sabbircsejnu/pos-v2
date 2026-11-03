// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { DASHBOARD_PATH } from 'src/app/app-config';

@Component({
  selector: 'app-error',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './error.component.html',
  styleUrls: ['../maintenance.scss', './error.component.scss']
})
export class ErrorComponent {
  default_path = DASHBOARD_PATH;
}
