// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from '../../../shared/shared.module';

@Component({
  selector: 'app-pricing',
  imports: [CommonModule, SharedModule],
  templateUrl: './pricing.component.html',
  styleUrls: ['./pricing.component.scss']
})
export class PricingComponent {
  // public props
  isChecked = true;
  togglePrice!: boolean;
  pricingPlans = [
    {
      name: 'Basic',
      services: '03',
      originalPrice: 729,
      discountedPrice: 69,
      button_type: true,
      features: [
        { label: 'One End Product', enabled: false },
        { label: 'No attribution required', enabled: false },
        { label: 'TypeScript', enabled: false },
        { label: 'Figma Design Resources', enabled: true },
        { label: 'Create Multiple Products', enabled: true },
        { label: 'Create a SaaS Project', enabled: true },
        { label: 'Resale Product', enabled: true },
        { label: 'Separate sale of our UI Elements?', enabled: true }
      ]
    },
    {
      name: 'Standard',
      services: '05',
      originalPrice: 1449,
      discountedPrice: 129,
      button_type: false,
      class: 'most-useable',
      margin: 'm-t-15',
      features: [
        { label: 'One End Product', enabled: false },
        { label: 'No attribution required', enabled: false },
        { label: 'TypeScript', enabled: false },
        { label: 'Figma Design Resources', enabled: false },
        { label: 'Create Multiple Products', enabled: false },
        { label: 'Create a SaaS Project', enabled: true },
        { label: 'Resale Product', enabled: true },
        { label: 'Separate sale of our UI Elements?', enabled: true }
      ]
    },
    {
      name: 'Premium',
      services: '08',
      originalPrice: 7089,
      discountedPrice: 599,
      button_type: true,
      features: [
        { label: 'One End Product', enabled: false },
        { label: 'No attribution required', enabled: false },
        { label: 'TypeScript', enabled: false },
        { label: 'Figma Design Resources', enabled: false },
        { label: 'Create Multiple Products', enabled: false },
        { label: 'Create a SaaS Project', enabled: false },
        { label: 'Resale Product', enabled: false },
        { label: 'Separate sale of our UI Elements?', enabled: false }
      ]
    }
  ];

  // public method
  toggleChecked(isChecked: boolean) {
    this.togglePrice = isChecked;
  }
}
