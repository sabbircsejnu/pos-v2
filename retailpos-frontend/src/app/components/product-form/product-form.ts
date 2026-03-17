import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { ProductService } from '../../services/product.service';
import { CategoryService } from '../../services/category.service';
import { CreateProductRequest, UpdateProductRequest } from '../../models/product.model';
import { Category } from '../../models/category.model';
import { CombinationManagerComponent } from '../combination-manager/combination-manager.component';

@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [CommonModule, FormsModule, CombinationManagerComponent],
  templateUrl: './product-form.html',
  styleUrl: './product-form.css',
})
export class ProductForm implements OnInit {
  isEditMode = signal(false);
  productId = signal<number | null>(null);
  
  // Form fields
  name = signal('');
  description = signal('');
  sku = signal('');
  barcode = signal('');
  categoryId = signal<number>(0);
  basePrice = signal<number>(0);
  costPrice = signal<number>(0);
  taxRate = signal<number>(0);
  hasVariants = signal(false);
  imageUrl = signal('');
  isActive = signal(true);
  
  // Categories
  categories = signal<Category[]>([]);
  
  // Validation
  errors = signal<{[key: string]: string}>({});
  submitError = signal<string | null>(null);
  isSubmitting = signal(false);

  constructor(
    public productService: ProductService,
    private categoryService: CategoryService,
    private router: Router,
    private route: ActivatedRoute
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
          this.sku.set(product.sku || '');
          this.barcode.set(product.barcode || '');
          this.categoryId.set(product.categoryId);
          this.basePrice.set(product.basePrice);
          this.costPrice.set(product.costPrice);
          this.taxRate.set(product.taxRate);
          this.hasVariants.set(product.hasVariants);
          this.imageUrl.set(product.imageUrl || '');
          this.isActive.set(product.isActive);
        }
      },
      error: (error) => {
        alert('Failed to load product');
        this.router.navigate(['/products']);
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

  createProduct(): void {
    const createRequest: CreateProductRequest = {
      name: this.name(),
      description: this.description() || undefined,
      sku: this.sku() || undefined,
      barcode: this.barcode() || undefined,
      categoryId: this.categoryId(),
      basePrice: this.basePrice(),
      costPrice: this.costPrice(),
      taxRate: this.taxRate(),
      hasVariants: this.hasVariants(),
      imageUrl: this.imageUrl() || undefined,
      isActive: this.isActive()
    };
    
    this.productService.create(createRequest).subscribe({
      next: () => {
        alert('Product created successfully');
        this.router.navigate(['/products']);
      },
      error: (error) => {
        this.submitError.set(error.error?.error || 'Failed to create product');
        this.isSubmitting.set(false);
      }
    });
  }

  updateProduct(): void {
    const updateRequest: UpdateProductRequest = {
      name: this.name(),
      description: this.description() || undefined,
      sku: this.sku() || undefined,
      barcode: this.barcode() || undefined,
      categoryId: this.categoryId(),
      basePrice: this.basePrice(),
      costPrice: this.costPrice(),
      taxRate: this.taxRate(),
      imageUrl: this.imageUrl() || undefined,
      isActive: this.isActive()
    };
    
    this.productService.update(this.productId()!, updateRequest).subscribe({
      next: () => {
        alert('Product updated successfully');
        this.router.navigate(['/products']);
      },
      error: (error) => {
        this.submitError.set(error.error?.error || 'Failed to update product');
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/products']);
  }
}
