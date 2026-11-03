// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-error-401',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './error-401.component.html',
  styleUrls: ['../maintenance.scss', './error-401.component.scss']
})
export class Error401Component {}
