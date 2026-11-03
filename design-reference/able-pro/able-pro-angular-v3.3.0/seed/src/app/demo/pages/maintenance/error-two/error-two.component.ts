// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-error-two',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './error-two.component.html',
  styleUrls: ['../maintenance.scss', './error-two.component.scss']
})
export class ErrorTwoComponent {}
