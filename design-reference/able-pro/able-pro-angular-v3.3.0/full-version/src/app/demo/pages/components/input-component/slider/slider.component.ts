// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-slider',
  imports: [CommonModule, SharedModule],
  templateUrl: './slider.component.html',
  styleUrls: ['./slider.component.scss']
})
export class SliderComponent {
  // public props
  disabled = false;
  max = 100;
  min = 0;
  showTicks = false;
  step = 1;
  thumbLabel = false;
  value = 10;

  // public method
  formatLabel(value: number): string {
    if (value >= 1000) {
      return Math.round(value / 1000) + 'k';
    }
    return `${value}`;
  }
}
