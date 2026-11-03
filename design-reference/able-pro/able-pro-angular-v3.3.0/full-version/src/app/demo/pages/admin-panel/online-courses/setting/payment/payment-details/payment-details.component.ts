// angular import
import { Component } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { CdkStepper, CdkStepperModule } from '@angular/cdk/stepper';

@Component({
  selector: 'app-payment-details',
  imports: [CdkStepperModule, NgTemplateOutlet, CommonModule],
  templateUrl: './payment-details.component.html',
  styleUrl: './payment-details.component.scss',
  providers: [{ provide: CdkStepper, useExisting: PaymentDetailsComponent }]
})
export class PaymentDetailsComponent extends CdkStepper {}
