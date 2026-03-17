import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CategoryService } from '../../services/category.service';
import { Category, CategoryTree } from '../../models/category.model';

@Component({
  selector: 'app-category-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './category-list.component.html',
  styleUrls: ['./category-list.component.css']
})
export class CategoryListComponent implements OnInit {
  viewMode = signal<'tree' | 'list'>('tree');
  searchQuery = signal('');
  showDeleteConfirm = signal(false);
  categoryToDelete = signal<Category | null>(null);
  successMessage = signal('');
  errorMessage = signal('');

  constructor(
    public categoryService: CategoryService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadCategories();
  }

  loadCategories(): void {
    if (this.viewMode() === 'tree') {
      this.categoryService.getCategoryTree().subscribe({
        error: (err) => {
          this.showError('Failed to load category tree');
        }
      });
    } else {
      this.categoryService.getAllCategories().subscribe({
        error: (err) => {
          this.showError('Failed to load categories');
        }
      });
    }
  }

  toggleViewMode(): void {
    this.viewMode.update(mode => mode === 'tree' ? 'list' : 'tree');
    this.loadCategories();
  }

  expandAll(): void {
    this.categoryService.expandAll(this.categoryService.categoryTree());
  }

  collapseAll(): void {
    this.categoryService.collapseAll(this.categoryService.categoryTree());
  }

  toggleNode(node: CategoryTree): void {
    this.categoryService.toggleNode(node);
  }

  createCategory(): void {
    this.router.navigate(['/categories/create']);
  }

  editCategory(id: number, event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    this.router.navigate(['/categories/edit', id]);
  }

  confirmDelete(category: Category | CategoryTree, event: Event): void {
    event.stopPropagation();
    // Convert CategoryTree to Category-like object if needed
    const cat = this.toCategoryFormat(category);
    this.categoryToDelete.set(cat);
    this.showDeleteConfirm.set(true);
  }

  toCategoryFormat(item: Category | CategoryTree): Category {
    // If it's already a full Category, return it
    if ('parentCategoryName' in item) {
      return item as Category;
    }
    // Convert CategoryTree to Category format
    const tree = item as CategoryTree;
    return {
      id: tree.id,
      name: tree.name,
      description: tree.description,
      parentCategoryId: tree.parentCategoryId,
      parentCategoryName: undefined,
      imageUrl: tree.imageUrl,
      displayOrder: tree.displayOrder,
      isActive: tree.isActive,
      createdAt: '',
      updatedAt: '',
      level: tree.level,
      childCount: tree.children?.length || 0,
      productCount: tree.productCount
    };
  }

  cancelDelete(): void {
    this.showDeleteConfirm.set(false);
    this.categoryToDelete.set(null);
  }

  deleteCategory(): void {
    const category = this.categoryToDelete();
    if (!category) return;

    this.categoryService.deleteCategory(category.id).subscribe({
      next: () => {
        this.showSuccess(`Category "${category.name}" deleted successfully`);
        this.showDeleteConfirm.set(false);
        this.categoryToDelete.set(null);
        this.loadCategories();
      },
      error: (err) => {
        this.showError(err.error?.error || 'Failed to delete category');
        this.showDeleteConfirm.set(false);
      }
    });
  }

  getFilteredCategories(): Category[] {
    const query = this.searchQuery().toLowerCase();
    if (!query) {
      return this.categoryService.categories();
    }
    return this.categoryService.categories().filter(cat =>
      cat.name.toLowerCase().includes(query) ||
      cat.description?.toLowerCase().includes(query)
    );
  }

  showSuccess(message: string): void {
    this.successMessage.set(message);
    setTimeout(() => this.successMessage.set(''), 3000);
  }

  showError(message: string): void {
    this.errorMessage.set(message);
    setTimeout(() => this.errorMessage.set(''), 5000);
  }

  getIndentation(level: number): string {
    return `${level * 24}px`;
  }
}
