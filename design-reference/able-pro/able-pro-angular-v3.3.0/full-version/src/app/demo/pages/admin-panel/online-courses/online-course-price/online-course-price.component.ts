// Angular imports
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

// project imports
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-online-course-price',
  imports: [SharedModule, CommonModule],
  templateUrl: './online-course-price.component.html',
  styleUrl: './online-course-price.component.scss'
})
export class OnlineCoursePriceComponent {
  // public props
  selectedImage: string = 'assets/images/admin/price-regular.svg';
  selectedPackageIndex: number = 1;

  // public method
  selectPrice(index: number) {
    this.selectedPackageIndex = index;
    this.selectedImage = this.priceList[index].imageSrc;
  }

  priceList = [
    {
      title: 'FREE',
      type: 'Basic Features',
      price: 0,
      imageSrc: 'assets/images/admin/price-free.svg',
      items: ['One End Product', 'No attribution required', 'TypeScript']
    },
    {
      title: 'REGULAR',
      type: 'Trending',
      price: 99,
      imageSrc: 'assets/images/admin/price-regular.svg',
      items: ['One End Product', 'No attribution required', 'TypeScript', 'Figma Design Resources', 'Create Multiple Products']
    },
    {
      title: 'PRO',
      type: 'For advanced',
      price: 199,
      imageSrc: 'assets/images/admin/price-pro.svg',
      items: [
        'One End Product',
        'No attribution required',
        'TypeScript',
        'Figma Design Resources',
        'Create Multiple Products',
        'Create a SaaS Project'
      ]
    },
    {
      title: 'Business',
      type: 'For advanced',
      price: 299,
      imageSrc: 'assets/images/admin/price-business.svg',
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
}
