// angular import
import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule } from '@angular/forms';
import { MatTableDataSource, MatTableModule } from '@angular/material/table';
import { Router, RouterModule } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { CouponDialogComponent } from './coupon-dialog/coupon-dialog.component';
import { AddressDialogComponent } from './address-dialog/address-dialog.component';
import { Products } from 'src/app/@theme/types/product';
import { ProductService } from '../product.service';

// third party
import { NgxMaskDirective, provideNgxMask } from 'ngx-mask';

@Component({
  selector: 'app-checkout',
  imports: [CommonModule, SharedModule, MatTableModule, RouterModule, NgxMaskDirective, FormsModule],
  templateUrl: './checkout.component.html',
  styleUrls: ['./checkout.component.scss'],
  providers: [provideNgxMask()]
})
export class CheckoutComponent implements OnInit {
  private _formBuilder = inject(FormBuilder);
  dialog = inject(MatDialog);
  private productService = inject(ProductService);
  private router = inject(Router);

  // public props
  firstFormGroup = this._formBuilder.group({});
  secondFormGroup = this._formBuilder.group({});
  thirdFormGroup = this._formBuilder.group({});
  totalItems: number;
  cartProduct: Products[] = [];
  totalAmount: number = 0;
  selectedValue: string;
  discountAmount: number = 0;

  // life cycle event
  ngOnInit() {
    this.productService.getCartItemCount().subscribe((count) => {
      this.totalItems = count;
    });

    this.productService.getCartItems().subscribe((cartItems) => {
      this.cartProduct = cartItems;
      this.dataSource = new MatTableDataSource(this.cartProduct);
    });
    this.dataSource = new MatTableDataSource(this.cartProduct);
    this.totalAmount = this.productService.getTotalCartPrice();
  }

  // cart Item Remove
  deleteItem(product: Products) {
    this.productService.removeFormCart(product);
  }

  // increase product quantity as pre user
  increaseQuantity(element: Products) {
    element.quantity++;
  }

  // decrease product quantity as pre user
  decreaseQuantity(element: Products) {
    if (element.quantity > 1) {
      element.quantity--;
    }
  }

  // get update quantity according increase and decrease
  // eslint-disable-next-line
  updateQuantity(product: Products, event: any) {
    const newQuantity = event.target.valueAsNumber; // Assuming the input is of type number
    this.productService.updateQuantity(product, newQuantity);
  }

  // get total price according product quantity
  getTotalPrice(product: Products): number {
    return this.productService.getTotalPrice(product);
  }

  // total cart value
  getTotalCartPrice(): number {
    return this.productService.getTotalCartPrice();
  }

  // go to product page if cart is empty
  exploreBag() {
    this.router.navigate(['/application/e-commerce/product']);
  }

  // public method
  displayedColumns: string[] = ['product', 'price', 'quantity', 'total', 'delete'];
  // eslint-disable-next-line
  dataSource: MatTableDataSource<any>;

  // get Coupon code
  couponModal(): void {
    const dialogRef = this.dialog.open(CouponDialogComponent, {
      width: '800px'
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.selectedValue = result;
      }
    });
  }

  // coupon code according discount value
  // eslint-disable-next-line
  couponMappings: any = {
    FLAT05: 5,
    SUB150: 20,
    ABLPRO50: 50,
    UPTO200: 10,
    ABLEPRO50: 50
  };

  // get discount amount according coupon code
  getDiscountPercentage(couponCode: string): number {
    return this.couponMappings[couponCode] || 0; // Default to 0 if the code is not found
  }

  // coupon code according apply discount
  applyCoupon() {
    if (this.selectedValue && this.selectedValue.trim() !== '') {
      // Non-empty coupon code is selected, apply discount based on the code
      const discountPercentage = this.getDiscountPercentage(this.selectedValue);
      this.discountAmount = (discountPercentage / 100) * this.productService.getTotalCartPrice();
    } else {
      // Empty or invalid coupon code, reset the discount
      this.discountAmount = 0;
    }

    // get final cart amount
    this.totalAmount = Math.max(0, this.getTotalCartPrice() - this.discountAmount);
  }

  // final tab on get coupon code according special offer
  onSelect(value: string): void {
    this.selectedValue = value;
  }

  addressDialog(): void {
    this.dialog.open(AddressDialogComponent, {
      // width: '950px'
    });
  }

  cardDetails = [
    {
      name: 'John Smith',
      img: 'assets/images/application/mastercard.png',
      lastDigit: '2599',
      cvv: '085',
      eDate: '05/26',
      checked: 'true'
    },
    {
      name: 'Jennifer winget',
      img: 'assets/images/application/visa.png',
      lastDigit: '2655',
      cvv: '899',
      eDate: '05/29',
      checked: 'false'
    }
  ];

  payment = [
    {
      title: 'Credit Card',
      discount: true,
      image: 'assets/images/application/card.png',
      checked: 'true'
    },
    {
      title: 'Pay With PayPal',
      discount: true,
      image: 'assets/images/application/paypal.png',
      checked: 'false'
    },
    {
      title: 'Cash On Delivery',
      discount: false,
      details: 'When you use this payment',
      checked: 'false'
    }
  ];

  deliveryAddress = [
    {
      checked: 'true',
      name: 'Ian Carpenter',
      type: '(Home)',
      address: '1754 Ureate Path, 695 Newga View, Seporcus, Rhode Island, Belgium - SA5 5BO',
      number: '+91 9856341272'
    },
    {
      checked: 'false',
      name: 'Ian Carpenter',
      type: '(office)',
      address: '1754 Ureate Path, 695 Newga View, Seporcus, Rhode Island, Belgium - SA5 5BO',
      number: '+91 1234567890'
    },
    {
      checked: 'false',
      name: 'Ian Carpenter',
      type: '(other)',
      address: '1754 Ureate Path, 695 Newga View, Seporcus, Rhode Island, Belgium - SA5 5BO',
      number: '+91 1234567890'
    },
    {
      checked: 'false',
      name: 'Ian Carpenter',
      type: '(work)',
      address: '1754 Ureate Path, 695 Newga View, Seporcus, Rhode Island, Belgium - SA5 5BO',
      number: '+91 1234567890'
    }
  ];
}
