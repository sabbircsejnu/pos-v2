import { Component, OnInit, ViewChild, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { ProductImageService } from '../../services/product-image.service';
import { CategoryService } from '../../services/category.service';
import { AlertService } from '../../services/alert.service';
import { CreateProductRequest, UpdateProductRequest } from '../../models/product.model';
import { Category } from '../../models/category.model';
import { CombinationManagerComponent } from '../combination-manager/combination-manager.component';
import { ProductImageUploaderComponent } from './product-image-uploader.component';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [CommonModule, FormsModule, CombinationManagerComponent, ProductImageUploaderComponent],
  templateUrl: './product-form.html',
  styleUrl: './product-form.css',
})
export class ProductForm implements OnInit {
  @ViewChild(ProductImageUploaderComponent) imageUploader?: ProductImageUploaderComponent;

  isEditMode = signal(false);
  productId = signal<number | null>(null);
  
  // Form fields
  name = signal('');
  description = signal('');
  productCode = signal('');
  sku = signal('');
  barcode = signal('');
  categoryId = signal<number>(0);
  basePrice = signal<number>(0);
  costPrice = signal<number>(0);
  taxRate = signal<number>(0);
  hasVariants = signal(false);
  status = signal<string>('active');

  // Future-ready UI fields (not persisted yet)
  brand = signal('');
  tags = signal('');
  trackInventory = signal(true);
  reorderLevel = signal<number>(10);
  openingStock = signal<number>(0);
  warehouse = signal('');
  
  // Categories
  categories = signal<Category[]>([]);
  
  // Validation
  errors = signal<{[key: string]: string}>({});
  submitError = signal<string | null>(null);
  isSubmitting = signal(false);

  private auth = inject(AuthService);
  canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));

  readonly setupChecklist = computed(() => {
    const basic = !!this.name().trim() && !!this.productCode().trim();
    const classification = this.categoryId() > 0;
    const pricing = this.basePrice() > 0 && (!this.canViewCost() || this.costPrice() >= 0);
    const variants = !this.hasVariants() || this.isEditMode();
    const media = this.isEditMode() && !!this.productId();

    return [
      { label: 'Basic Information', complete: basic },
      { label: 'Category', complete: classification },
      { label: 'Pricing', complete: pricing },
      { label: 'Variants', complete: variants },
      { label: 'Media', complete: media },
    ];
  });

  readonly setupPercent = computed(() => {
    const checklist = this.setupChecklist();
    if (checklist.length === 0) return 0;
    const done = checklist.filter(item => item.complete).length;
    return Math.round((done / checklist.length) * 100);
  });

  readonly computedMargin = computed(() => {
    if (!this.canViewCost()) return null;
    const sell = this.basePrice();
    const cost = this.costPrice();
    if (sell <= 0 || cost < 0) return null;
    return (((sell - cost) / sell) * 100);
  });

  constructor(
    public productService: ProductService,
    private productImageService: ProductImageService,
    private categoryService: CategoryService,
    private alertService: AlertService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute,
    public currency: CurrencyService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.loadCategories();
    
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.productId.set(+id);
      this.loadProduct(+id);
    }
  }

  loadCategories(): void {
    this.categoryService.getAllCategories().subscribe({
      next: (response: any) => {
        this.categories.set(response.data || []);
      }
    });
  }

  loadProduct(id: number): void {
    this.productService.getById(id).subscribe({
      next: (response) => {
        const product = response.data;
        if (product) {
          this.name.set(product.name);
          this.description.set(product.description || '');
          this.productCode.set(product.productCode || '');
          this.sku.set(product.sku || '');
          this.barcode.set(product.barcode || '');
          this.categoryId.set(product.categoryId);
          this.basePrice.set(product.basePrice);
          this.costPrice.set(product.costPrice);
          this.taxRate.set(product.taxRate);
          this.hasVariants.set(product.hasVariants);
          this.status.set(product.status ?? 'active');
        }
      },
      error: (error) => {
        alert('Failed to load product');
        this.location.back();
      }
    });
  }

  generateSku(): void {
    if (!this.name()) {
      alert('Please enter a product name first');
      return;
    }
    
    this.productService.generateSku(this.name()).subscribe({
      next: (response) => {
        this.sku.set(response.data);
      },
      error: () => {
        alert('Failed to generate SKU');
      }
    });
  }

  validate(): boolean {
    const newErrors: {[key: string]: string} = {};
    
    if (!this.name().trim()) {
      newErrors['name'] = 'Product name is required';
    }
    
    if (this.categoryId() === 0) {
      newErrors['categoryId'] = 'Please select a category';
    }
    
    if (this.basePrice() <= 0) {
      newErrors['basePrice'] = 'Base price must be greater than 0';
    }
    
    if (this.taxRate() < 0 || this.taxRate() > 100) {
      newErrors['taxRate'] = 'Tax rate must be between 0 and 100';
    }
    
    this.errors.set(newErrors);
    return Object.keys(newErrors).length === 0;
  }

  onSubmit(): void {
    if (!this.validate()) {
      return;
    }
    
    this.isSubmitting.set(true);
    this.submitError.set(null);
    
    if (this.isEditMode()) {
      this.updateProduct();
    } else {
      this.createProduct();
    }
  }

  saveDraft(): void {
    this.status.set('draft');
    this.onSubmit();
  }

  createProduct(): void {
    const createRequest: CreateProductRequest = {
      name: this.name(),
      description: this.description() || undefined,
      productCode: this.productCode() || undefined,
      sku: this.sku() || undefined,
      barcode: this.barcode() || undefined,
      categoryId: this.categoryId(),
      basePrice: this.basePrice(),
      costPrice: this.costPrice(),
      taxRate: this.taxRate(),
      hasVariants: this.hasVariants(),
      status: this.status()
    };

    this.productService.create(createRequest).subscribe({
      next: (response) => {
        const created = (response as any)?.data;
        const newId = created?.id as number | undefined;
        if (!newId) {
          this.isSubmitting.set(false);
          this.router.navigate(['/products']);
          return;
        }
        this.productId.set(newId);
        const pendingFiles = this.imageUploader?.getPendingFiles() ?? [];
        if (pendingFiles.length === 0) {
          this.isSubmitting.set(false);
          this.router.navigate(['/products', 'edit', newId], {
            queryParams: this.route.snapshot.queryParams,
          });
          return;
        }
        this.uploadPendingImages(newId, pendingFiles, 0, []);
      },
      error: (error) => {
        this.submitError.set(this.errorHandler.extractErrorMessage(error));
        this.isSubmitting.set(false);
      }
    });
  }

  private uploadPendingImages(productId: number, files: File[], index: number, failedNames: string[]): void {
    if (index >= files.length) {
      this.imageUploader?.clearPendingFiles();
      this.isSubmitting.set(false);
      if (failedNames.length > 0) {
        this.alertService.warning(
          `Product saved, but ${failedNames.length} image(s) could not be uploaded: ${failedNames.join(', ')}. You can retry from the Media section.`,
          'Some Images Failed'
        );
      }
      this.router.navigate(['/products', 'edit', productId], {
        queryParams: this.route.snapshot.queryParams,
      });
      return;
    }
    const file = files[index];
    this.productImageService.upload(productId, file).subscribe({
      next: () => this.uploadPendingImages(productId, files, index + 1, failedNames),
      error: () => this.uploadPendingImages(productId, files, index + 1, [...failedNames, file.name]),
    });
  }

  updateProduct(): void {
    const updateRequest: UpdateProductRequest = {
      name: this.name(),
      description: this.description() || undefined,
      productCode: this.productCode() || undefined,
      sku: this.sku() || undefined,
      barcode: this.barcode() || undefined,
      categoryId: this.categoryId(),
      basePrice: this.basePrice(),
      costPrice: this.costPrice(),
      taxRate: this.taxRate(),
      status: this.status()
    };

    this.productService.update(this.productId()!, updateRequest).subscribe({
      next: () => {
        this.alertService.success('Product updated successfully');
        this.location.back();
      },
      error: (error) => {
        this.submitError.set(this.errorHandler.extractErrorMessage(error));
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.location.back();
  }
}
