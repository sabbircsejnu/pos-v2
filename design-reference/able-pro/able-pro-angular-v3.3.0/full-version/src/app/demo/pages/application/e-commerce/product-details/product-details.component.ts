// angular imports
import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';

// project import
import { ProductService } from 'src/app/demo/pages/application/e-commerce/product.service';
import { Products } from 'src/app/@theme/types/product';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { CommentList } from 'src/app/fake-data/comment-list';
import { ProductRating } from 'src/app/fake-data/product-rating';
import { productFeatures } from 'src/app/fake-data/product-features';
import { StarRatingComponent } from 'src/app/@theme/components/star-rating/star-rating.component';

@Component({
  selector: 'app-product-details',
  imports: [CommonModule, SharedModule, StarRatingComponent, RouterModule],
  templateUrl: './product-details.component.html',
  styleUrls: ['./product-details.component.scss']
})
export class ProductDetailsComponent implements OnInit {
  private productService = inject(ProductService);
  private activeRoute = inject(ActivatedRoute);
  router = inject(Router);

  // public pros
  currentSlide = '1';
  productData: Products[];
  selectedProduct: Products;
  inputNumber = 0;
  img: string;
  ratings = 3;
  starCount = 5;
  isZoomed: boolean = false;
  cartItemCount: number;
  quantityToAdd: number = 1;

  zoomImage(): void {
    this.isZoomed = !this.isZoomed;
  }

  // life cycle event
  ngOnInit() {
    this.productService.getProduct();
    this.productService.product.subscribe((data) => {
      this.productData = data;
      const productId = this.activeRoute.snapshot.paramMap.get('productId');
      this.selectedProduct = this.productData.filter((product) => product.id?.toString() === productId)[0];
      this.img = 'assets/images/e-commerce/' + this.selectedProduct.image;
    });
    this.productService.getCartItemCount().subscribe((count) => {
      this.cartItemCount = count;
    });
  }

  // redirect to checkout page
  goToBuy(product: Products) {
    this.productService.CartItems(product, this.quantityToAdd);
    this.router.navigate(['/application/e-commerce/checkout']);
  }

  // product add to cart
  addToCart(product: Products) {
    this.productService.CartItems(product, this.quantityToAdd);
  }

  // product rating
  onRatingChanged(ratings: number) {
    this.ratings = ratings;
  }

  // public method
  showImages(event: { target: { src: string } }) {
    if (event.target.src) {
      this.img = event.target.src;
    }
  }

  // product quantity add into cart
  increaseQuantity() {
    this.quantityToAdd++;
  }
  decreaseQuantity() {
    if (this.quantityToAdd > 1) {
      this.quantityToAdd--;
    }
  }

  // checkout page redirect
  goToCheckout() {
    this.router.navigate(['/application/e-commerce/checkout']);
  }

  features = productFeatures;
  rating = ProductRating;
  commentList = CommentList;

  specifications = [
    {
      title: 'Wearable Device Type',
      details: 'Smart Band'
    },
    {
      title: 'Compatible Devices',
      details: 'Smartphones'
    },
    {
      title: 'Ideal For',
      details: 'Unisex'
    }
  ];

  manufacturer = [
    {
      title: 'Brand',
      details: 'Smart Band'
    },
    {
      title: 'Model Series',
      details: 'Watch SE'
    },
    {
      title: 'Model Number',
      details: 'MYDT2HN/A'
    }
  ];

  // eslint-disable-next-line
  toggleWhitelist(item: any) {
    // Convert the value to a boolean if it's not already
    const isWhitelistedBool = item.isWhitelisted === true || item.isWhitelisted === 'true';

    // Toggle the value
    item.isWhitelisted = !isWhitelistedBool;

    // Optionally, perform additional actions like emitting an event or making an API call
    console.log(`Whitelist status updated: ${item.isWhitelisted}`);
  }
}
