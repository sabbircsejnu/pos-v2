// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-sticky-form',
  imports: [CommonModule, SharedModule],
  templateUrl: './sticky-form.component.html',
  styleUrls: ['./sticky-form.component.scss']
})
export class StickyFormComponent {}
