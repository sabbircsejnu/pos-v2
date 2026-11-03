// angular import
import { Component, OnInit, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDrawer, MatDrawerMode } from '@angular/material/sidenav';
import { Router, RouterModule } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ProductService } from 'src/app/demo/pages/application/e-commerce/product.service';
import { Products } from 'src/app/@theme/types/product';
import { MIN_WIDTH_1025PX, MAX_WIDTH_1024PX, MAX_WIDTH_1399PX, MIN_WIDTH_1400PX } from 'src/app/@theme/const';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

@Component({
  selector: 'app-product',
  imports: [CommonModule, SharedModule, RouterModule, NgxScrollbar],
  templateUrl: './product.component.html',
  styleUrls: ['./product.component.scss']
})
export class ProductComponent implements OnInit {
  private breakpointObserver = inject(BreakpointObserver);
  private productService = inject(ProductService);
  router = inject(Router);

  // public props
  readonly commerce = viewChild<MatDrawer>('commerce');
  modeValue: MatDrawerMode = 'side';
  status: string = 'true';
  max = 1000;
  min = 0;
  thumbLabel = true;
  value = 300;
  productData: Products[];
  cartItems: Products[] = [];
  isWhitelisted = false;
  selectedGender: string;
  selectedCategory: string;
  selectedColor: string;
  cartItemCount: number;
  quantityToAdd: number = 1;

  // life cycle event
  ngOnInit() {
    // this use for filter drawer for media
    this.breakpointObserver.observe([MIN_WIDTH_1025PX, MAX_WIDTH_1024PX]).subscribe((result) => {
      if (result.breakpoints[MAX_WIDTH_1024PX]) {
        this.modeValue = 'over';
      } else if (result.breakpoints[MIN_WIDTH_1025PX]) {
        this.modeValue = 'side';
      }
    });
    this.breakpointObserver.observe([MIN_WIDTH_1400PX, MAX_WIDTH_1399PX]).subscribe((result) => {
      if (result.breakpoints[MAX_WIDTH_1399PX]) {
        this.status = 'false';
      } else if (result.breakpoints[MIN_WIDTH_1400PX]) {
        this.status = 'true';
      }
    });

    // product data
    this.productService.getProduct();
    this.productService.product.subscribe((data) => {
      this.productData = data;
    });
    this.productService.getCartItemCount().subscribe((count) => {
      this.cartItemCount = count;
    });
  }

  //filter item
  genderOptions = ['male', 'female', 'kids'];
  categoryOptions = ['', 'fashion', 'electronics', 'watch', 'book', 'toy'];
  colorOptions = [
    { value: 'primary200', color: 'text-primary-200', tooltip: 'Light Primary' },
    { value: 'primaryDark', color: 'text-primary-700', tooltip: 'Dark Primary' },
    { value: 'secondary200', color: 'text-accent-300', tooltip: 'Light Secondary' },
    { value: 'secondaryMain', color: 'text-accent-600', tooltip: 'Secondary' },
    { value: 'successLight', color: 'text-success-200', tooltip: 'Light Green' },
    { value: 'successMain', color: 'text-success-500', tooltip: 'Green' },
    { value: 'successDark', color: 'text-success-700', tooltip: 'Dark Green' },
    { value: 'errorLight', color: 'text-warn-200', tooltip: 'Light Red' },
    { value: 'errorMain', color: 'text-warn-500', tooltip: 'Red' },
    { value: 'errorDark', color: 'text-warn-700', tooltip: 'Dark Red' },
    { value: 'warningMain', color: 'text-warning-500', tooltip: 'Yellow' },
    { value: 'warningDark', color: 'text-warning-700', tooltip: 'Dark Yellow' }
  ];

  applyFilters() {
    this.productService.applyFilters({
      gender: this.selectedGender,
      category: this.selectedCategory,
      color: this.selectedColor
    });
  }

  // Function to reset filters
  resetFilters() {
    this.selectedGender = '';
    this.selectedCategory = '';
    this.selectedColor = '';

    this.productService.resetFilters();
  }

  // Function to handle gender filter toggling
  toggleGender(gender: string) {
    if (this.selectedGender === gender) {
      // If the gender is already selected, remove it to reset the filter
      this.selectedGender = '';
    } else {
      // If the gender is not selected, set it to the filter
      this.selectedGender = gender;
    }
    this.applyFilters(); // Apply the filter after each toggle
  }

  // Function to handle category filter toggling
  toggleCategory(value: string) {
    if (this.selectedCategory === value) {
      // If the category is already selected, remove it to reset the filter
      this.selectedCategory = '';
    } else {
      // If the category is not selected, set it to the filter
      this.selectedCategory = value;
    }
    this.applyFilters(); // Apply the filter after each toggle
  }

  // Function to handle color filter toggling
  toggleColor(color: string) {
    if (this.selectedColor === color) {
      // If the color is already selected, remove it to reset the filter
      this.selectedColor = '';
    } else {
      // If the color is not selected, set it to the filter
      this.selectedColor = color;
    }
    this.applyFilters(); // Apply the filter after each toggle
  }

  // product add into Cart
  addToCart(product: Products) {
    this.productService.CartItems(product, this.quantityToAdd);
  }

  // checkout page redirect
  goToCheckout() {
    this.router.navigate(['/application/e-commerce/checkout']);
  }

  // eslint-disable-next-line
  toggleWhitelist(product: any) {
    product.isWhitelisted = !product.isWhitelisted;
  }
}
