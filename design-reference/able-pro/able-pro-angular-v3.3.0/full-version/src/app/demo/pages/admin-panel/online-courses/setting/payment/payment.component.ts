// angular import
import { Component } from '@angular/core';
import { CdkStepperModule } from '@angular/cdk/stepper';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { PaymentDetailsComponent } from './payment-details/payment-details.component';

@Component({
  selector: 'app-payment',
  imports: [SharedModule, PaymentDetailsComponent, CdkStepperModule],
  templateUrl: './payment.component.html',
  styleUrl: './payment.component.scss'
})
export class PaymentComponent {}
