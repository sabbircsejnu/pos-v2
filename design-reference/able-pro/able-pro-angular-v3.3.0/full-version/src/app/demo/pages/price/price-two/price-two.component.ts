// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-price-two',
  imports: [CommonModule, SharedModule],
  templateUrl: './price-two.component.html',
  styleUrls: ['./price-two.component.scss']
})
export class PriceTwoComponent {
  isChecked = true;
  // eslint-disable-next-line
  prices: any = {
    price1: 729,
    price2: 1449,
    price3: 7089
  };
  selectedPackageIndex = 0;

  packages = [
    {
      name: 'Basic',
      value: '03',
      popular: false,
      priceValue: '$69',
      items: ['One End Product', 'No attribution required', 'TypeScript']
    },
    {
      name: 'Standard',
      value: '05',
      color: 'bg-success-500',
      popular: true,
      show: 'popular',
      priceValue: '$129',
      items: ['One End Product', 'No attribution required', 'TypeScript', 'Figma Design Resources', 'Create Multiple Products']
    },
    {
      name: 'Premium',
      value: '08',
      popular: false,
      priceValue: '$599',
      items: [
        'One End Product',
        'No attribution required',
        'TypeScript',
        'Figma Design Resources',
        'Create Multiple Products',
        'Create a SaaS Project',
        'Resale Product',
        'Separate sale of our UI Elements?'
      ]
    }
  ];

  toggleChecked() {
    if (this.isChecked) {
      this.prices.price1 = 69;
      this.prices.price2 = 129;
      this.prices.price3 = 599;
    } else {
      this.prices.price1 = 729;
      this.prices.price2 = 1449;
      this.prices.price3 = 7089;
    }
  }

  selectPackage(index: number): void {
    this.selectedPackageIndex = index;
  }
}
