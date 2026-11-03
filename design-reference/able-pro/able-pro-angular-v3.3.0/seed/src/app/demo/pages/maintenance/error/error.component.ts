// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-error',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './error.component.html',
  styleUrls: ['../maintenance.scss', './error.component.scss']
})
export class ErrorComponent {}
