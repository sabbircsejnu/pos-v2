import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CategoryService } from '../../services/category.service';
import { CreateCategoryRequest, UpdateCategoryRequest, Category } from '../../models/category.model';

@Component({
  selector: 'app-category-form',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './category-form.component.html',
  styleUrls: ['./category-form.component.css']
})
export class CategoryFormComponent implements OnInit {
  isEditMode = signal(false);
  categoryId = signal<number | null>(null);
  availableParents = signal<Category[]>([]);
  
  // Form fields
  name = signal('');
  description = signal('');
  parentCategoryId = signal<number | undefined>(undefined);
  imageUrl = signal('');
  displayOrder = signal(0);
  isActive = signal(true);
  
  // UI state
  isSubmitting = signal(false);
  errorMessage = signal('');

  constructor(
    private categoryService: CategoryService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    // Load available parent categories
    this.loadParentCategories();
    
    // Check if edit mode
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.categoryId.set(+id);
      this.loadCategory(+id);
    }
  }

  loadParentCategories(): void {
    this.categoryService.getAllCategories().subscribe({
      next: (response) => {
        // Filter out current category if editing (to prevent circular reference)
        const categories = this.isEditMode()
          ? response.data.filter(c => c.id !== this.categoryId())
          : response.data;
        this.availableParents.set(categories);
      },
      error: (err) => {
        this.errorMessage.set('Failed to load parent categories');
      }
    });
  }

  loadCategory(id: number): void {
    this.categoryService.getCategoryById(id).subscribe({
      next: (response) => {
        const category = response.data;
        this.name.set(category.name);
        this.description.set(category.description || '');
        this.parentCategoryId.set(category.parentCategoryId);
        this.imageUrl.set(category.imageUrl || '');
        this.displayOrder.set(category.displayOrder);
        this.isActive.set(category.isActive);
      },
      error: (err) => {
        this.errorMessage.set('Failed to load category');
      }
    });
  }

  onSubmit(): void {
    // Validation
    if (!this.name().trim()) {
      this.errorMessage.set('Category name is required');
      return;
    }

    if (this.name().length < 2 || this.name().length > 100) {
      this.errorMessage.set('Category name must be between 2 and 100 characters');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set('');

    const request = {
      name: this.name().trim(),
      description: this.description().trim() || undefined,
      parentCategoryId: this.parentCategoryId() || undefined,
      imageUrl: this.imageUrl().trim() || undefined,
      displayOrder: this.displayOrder(),
      isActive: this.isActive()
    };

    if (this.isEditMode() && this.categoryId()) {
      // Update existing category
      this.categoryService.updateCategory(this.categoryId()!, request as UpdateCategoryRequest).subscribe({
        next: () => {
          this.router.navigate(['/categories']);
        },
        error: (err) => {
          this.errorMessage.set(err.error?.error || 'Failed to update category');
          this.isSubmitting.set(false);
        }
      });
    } else {
      // Create new category
      this.categoryService.createCategory(request as CreateCategoryRequest).subscribe({
        next: () => {
          this.router.navigate(['/categories']);
        },
        error: (err) => {
          this.errorMessage.set(err.error?.error || 'Failed to create category');
          this.isSubmitting.set(false);
        }
      });
    }
  }

  cancel(): void {
    this.router.navigate(['/categories']);
  }

  getCategoryIndentation(level: number): string {
    return '—'.repeat(level) + ' ';
  }
}
