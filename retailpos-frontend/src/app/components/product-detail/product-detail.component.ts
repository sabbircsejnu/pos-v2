import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { ProductImageService } from '../../services/product-image.service';
import { AlertService } from '../../services/alert.service';
import { Product } from '../../models/product.model';
import { ProductImage } from '../../models/product-image.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { ImageLightboxComponent } from '../product-form/image-lightbox.component';

@Component({
  selector: 'app-product-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, AppCurrencyPipe, ImageLightboxComponent],
  templateUrl: './product-detail.component.html',
  styleUrl: './product-detail.component.css',
})
export class ProductDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private location = inject(Location);
  private productService = inject(ProductService);
  auth = inject(AuthService);
  canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));
  private imageSvc = inject(ProductImageService);
  private alertService = inject(AlertService);

  product = signal<Product | null>(null);
  images = signal<ProductImage[]>([]);
  isLoading = signal(false);
  imagesLoading = signal(false);
  error = signal<string | null>(null);
  productId = signal<number | null>(null);

  lightboxImage = signal<{ medium: string; original: string } | null>(null);
  activeImageUrl = signal<string | null>(null);
  listQueryParams = signal<Record<string, string | number | boolean>>({});

  ngOnInit(): void {
    this.listQueryParams.set(this.route.snapshot.queryParams);
    const id = this.route.snapshot.paramMap.get('id');
    if (id && !isNaN(+id)) {
      this.productId.set(+id);
      this.loadProduct(+id);
    } else {
      this.error.set('Invalid product ID.');
    }
  }

  loadProduct(id: number): void {
    this.isLoading.set(true);
    this.error.set(null);
    this.productService.getById(id).subscribe({
      next: (response) => {
        const p: Product = response.data;
        this.product.set(p);
        this.isLoading.set(false);
        if (p.primaryImageMedium) {
          this.activeImageUrl.set(this.imageSvc.resolveUrl(p.primaryImageMedium));
        }
        this.loadImages(id);
      },
      error: () => {
        this.error.set('Failed to load product details.');
        this.isLoading.set(false);
      },
    });
  }

  loadImages(id: number): void {
    this.imagesLoading.set(true);
    this.imageSvc.list(id).subscribe({
      next: (imgs) => {
        this.images.set(imgs);
        const primary = imgs.find((i) => i.isPrimary) ?? imgs[0];
        if (primary) {
          this.activeImageUrl.set(this.imageSvc.resolveUrl(primary.mediumUrl));
        }
        this.imagesLoading.set(false);
      },
      error: () => this.imagesLoading.set(false),
    });
  }

  editProduct(): void {
    if (this.productId()) {
      this.router.navigate(['/products/edit', this.productId()], {
        queryParams: this.listQueryParams(),
      });
    }
  }

  goBack(): void {
    this.location.back();
  }

  selectImage(img: ProductImage): void {
    this.activeImageUrl.set(this.imageSvc.resolveUrl(img.mediumUrl));
  }

  openLightbox(): void {
    const url = this.activeImageUrl();
    if (url) {
      this.lightboxImage.set({ medium: url, original: url });
    }
  }

  resolveImage(path?: string | null): string {
    return this.imageSvc.resolveUrl(path);
  }

  get finalPrice(): number {
    const p = this.product();
    return p ? p.basePrice : 0;
  }

  get profitMargin(): number {
    const p = this.product();
    if (!p || p.basePrice <= 0 || p.costPrice <= 0) return 0;
    return ((p.basePrice - p.costPrice) / p.basePrice) * 100;
  }
}
