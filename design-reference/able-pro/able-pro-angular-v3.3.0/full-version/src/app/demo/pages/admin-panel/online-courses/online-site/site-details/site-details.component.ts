// angular import
import { CdkStepper, CdkStepperModule } from '@angular/cdk/stepper';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { Component } from '@angular/core';

@Component({
  selector: 'app-site-details',
  imports: [CdkStepperModule, NgTemplateOutlet, CommonModule],
  templateUrl: './site-details.component.html',
  styleUrl: './site-details.component.scss',
  providers: [{ provide: CdkStepper, useExisting: SiteDetailsComponent }]
})
export class SiteDetailsComponent extends CdkStepper {
  // public method
  onClick(index: number): void {
    this.selectedIndex = index;
  }
}
