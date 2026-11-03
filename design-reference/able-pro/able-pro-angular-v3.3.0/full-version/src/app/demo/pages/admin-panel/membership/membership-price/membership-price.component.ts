// angular import
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-membership-price',
  imports: [SharedModule],
  templateUrl: './membership-price.component.html',
  styleUrl: './membership-price.component.scss'
})
export class MembershipPriceComponent {
  // public method
  priceList = [
    {
      border: 'border-success',
      background: 'bg-success-50',
      name: 'Casual',
      price: '$50/',
      color: 'text-success-500',
      includeItem: [
        {
          title: 'Full Facility Access'
        },
        {
          title: 'Meals plans'
        },
        {
          title: '10% Discounts'
        },
        {
          title: 'Cancel anytime'
        }
      ]
    },
    {
      border: 'border-primary',
      background: 'bg-primary-50',
      name: 'Addicted',
      price: '$150/',
      color: 'text-primary-500',
      includeItem: [
        {
          title: 'Full Facility Access'
        },
        {
          title: 'Meals plans'
        },
        {
          title: '50% Discounts'
        },
        {
          title: 'Cancel anytime'
        },
        {
          title: 'Basic feature'
        }
      ]
    },
    {
      border: 'border-warn',
      background: 'bg-warn-50',
      name: 'Addicted',
      price: '$200/',
      color: 'text-warn-500',
      includeItem: [
        {
          title: 'Full Facility Access'
        },
        {
          title: 'Meals plans'
        },
        {
          title: 'Primmum feature'
        },
        {
          title: '75% Discounts'
        },
        {
          title: 'Cancel anytime'
        },
        {
          title: 'Online booking'
        }
      ]
    }
  ];
}
