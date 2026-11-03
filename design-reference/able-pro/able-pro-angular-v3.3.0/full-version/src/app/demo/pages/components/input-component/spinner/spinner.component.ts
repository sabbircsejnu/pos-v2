// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ThemePalette } from '@angular/material/core';
import { ProgressSpinnerMode } from '@angular/material/progress-spinner';
// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-spinner',
  imports: [CommonModule, SharedModule],
  templateUrl: './spinner.component.html',
  styleUrls: ['./spinner.component.scss']
})
export class SpinnerComponent {
  // public props
  color: ThemePalette = 'primary';
  mode: ProgressSpinnerMode = 'determinate';
  value = 50;
}
