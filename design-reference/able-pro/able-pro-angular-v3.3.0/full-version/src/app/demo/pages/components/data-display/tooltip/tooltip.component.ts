// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl } from '@angular/forms';
import { MAT_TOOLTIP_DEFAULT_OPTIONS, MatTooltipDefaultOptions, TooltipPosition } from '@angular/material/tooltip';
// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

/** Custom options the configure the tooltip's default show/hide delays. */
export const myCustomTooltipDefaults: MatTooltipDefaultOptions = {
  showDelay: 1000,
  hideDelay: 1000,
  touchendHideDelay: 1000
};

@Component({
  selector: 'app-tooltip',
  imports: [CommonModule, SharedModule],
  templateUrl: './tooltip.component.html',
  styleUrls: ['./tooltip.component.scss'],
  providers: [{ provide: MAT_TOOLTIP_DEFAULT_OPTIONS, useValue: myCustomTooltipDefaults }]
})
export class TooltipComponent {
  // public props
  showDelay = new FormControl(1000);
  hideDelay = new FormControl(2000);
  disabled = new FormControl(false);
  message = new FormControl('Info about the action');
  positionOptions: TooltipPosition[] = ['after', 'before', 'above', 'below', 'left', 'right'];
  position = new FormControl(this.positionOptions[0]);
}
