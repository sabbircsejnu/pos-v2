// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { DASHBOARD_PATH } from 'src/app/app-config';

@Component({
  selector: 'app-error-two',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './error-two.component.html',
  styleUrls: ['../maintenance.scss', './error-two.component.scss']
})
export class ErrorTwoComponent {
  default_path = DASHBOARD_PATH;
}
