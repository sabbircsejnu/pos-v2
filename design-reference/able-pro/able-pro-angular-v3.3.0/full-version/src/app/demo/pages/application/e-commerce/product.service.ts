import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { BehaviorSubject, Observable, Subject } from 'rxjs';

import { Products, productResponse } from '../../../../@theme/types/product';
import { environment } from 'src/environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ProductService {
  private http = inject(HttpClient);

  // product data
  private productSubject = new Subject<Products[]>();
  product: Observable<Products[]> = this.productSubject.asObservable();
  productData: Products[] = [];

  // cart product Count
  private cartItemCountSubject = new BehaviorSubject<number>(0);

  // Cart Product Data
  private cartItemsSubject = new BehaviorSubject<Products[]>([]);
  private cartItems: Products[] = [];

  private quantitySubject = new BehaviorSubject<number>(0);

  // product Data get form API
  getProduct(): void {
    this.http.get<productResponse>(`${environment.apiUrl}/api/products/list`).subscribe((data) => {
      this.productSubject.next(data.products);
      this.productData = data.products;
    });
  }

  // eslint-disable-next-line
  applyFilters(filters: { gender?: string; category?: any; color?: any }) {
    let filteredProducts = [...this.productData];

    if (filters.gender) {
      filteredProducts = filteredProducts.filter((product) => product.gender === filters.gender);
    }

    if (filters.category) {
      filteredProducts = filteredProducts.filter((product) => product.categories?.includes(filters.category));
    }

    if (filters.color) {
      filteredProducts = filteredProducts.filter((product) => product.colors?.includes(filters.color));
    }

    this.productSubject.next(filteredProducts);
  }

  // Function to reset filters
  resetFilters() {
    this.productSubject.next(this.productData);
  }

  // cart Product data
  CartItems(product: Products, quantityToAdd: number) {
    const existingProduct = this.cartItems.find((x) => x.id === product.id);
    if (existingProduct) {
      existingProduct.quantity++;
    } else {
      this.cartItems.push({ ...product, quantity: quantityToAdd });
    }
    this.updateCart();
  }

  // product data remove form cart
  removeFormCart(product: Products) {
    const existingProductIndex = this.cartItems.findIndex((p) => p.id === product.id);
    if (existingProductIndex !== -1) {
      this.cartItems.splice(existingProductIndex, 1);
      this.updateCart();
    }
  }

  getCartItems() {
    return this.cartItemsSubject.asObservable();
  }

  getCartItemCount() {
    return this.cartItemCountSubject.asObservable();
  }

  getQuantity() {
    return this.quantitySubject.asObservable();
  }

  getTotalCartPrice(): number {
    return this.cartItems.reduce((total, product) => total + this.getTotalPrice(product), 0);
  }

  // cart Product Update
  private updateCart() {
    this.cartItemCountSubject.next(this.cartItems.length);
    this.cartItemsSubject.next([...this.cartItems]);
  }

  updateQuantity(product: Products, quantity: number) {
    const existingProduct = this.cartItems.find((x) => x.id === product.id);
    if (existingProduct) {
      existingProduct.quantity = quantity;
      this.updateCart();
    }
  }

  getTotalPrice(product: Products): number {
    return product.offerPrice! * product.quantity;
  }
}
