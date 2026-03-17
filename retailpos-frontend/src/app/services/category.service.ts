import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Category,
  CategoryTree,
  CreateCategoryRequest,
  UpdateCategoryRequest,
  MoveCategoryRequest,
  CategoryResponse,
  CategoryListResponse,
  CategoryTreeResponse
} from '../models/category.model';

@Injectable({
  providedIn: 'root'
})
export class CategoryService {
  private apiUrl = `${environment.apiUrl}/categories`;

  // Reactive state with signals
  categories = signal<Category[]>([]);
  categoryTree = signal<CategoryTree[]>([]);
  selectedCategory = signal<Category | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /**
   * Get all categories (flat list)
   */
  getAllCategories(): Observable<CategoryListResponse> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.get<CategoryListResponse>(this.apiUrl).pipe(
      tap({
        next: (response) => {
          this.categories.set(response.data);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to load categories');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Get category tree (hierarchical)
   */
  getCategoryTree(): Observable<CategoryTreeResponse> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.get<CategoryTreeResponse>(`${this.apiUrl}/tree`).pipe(
      tap({
        next: (response) => {
          this.categoryTree.set(response.data);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to load category tree');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Get category by ID
   */
  getCategoryById(id: number): Observable<CategoryResponse> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.get<CategoryResponse>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: (response) => {
          this.selectedCategory.set(response.data);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to load category');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Get children of a category
   */
  getCategoryChildren(parentId: number): Observable<CategoryListResponse> {
    return this.http.get<CategoryListResponse>(`${this.apiUrl}/${parentId}/children`);
  }

  /**
   * Create new category
   */
  createCategory(request: CreateCategoryRequest): Observable<CategoryResponse> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.post<CategoryResponse>(this.apiUrl, request).pipe(
      tap({
        next: () => {
          this.isLoading.set(false);
          // Refresh categories list
          this.getAllCategories().subscribe();
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to create category');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Update existing category
   */
  updateCategory(id: number, request: UpdateCategoryRequest): Observable<CategoryResponse> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.put<CategoryResponse>(`${this.apiUrl}/${id}`, request).pipe(
      tap({
        next: () => {
          this.isLoading.set(false);
          // Refresh categories list
          this.getAllCategories().subscribe();
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to update category');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Delete category
   */
  deleteCategory(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.delete(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => {
          this.isLoading.set(false);
          // Remove from local state
          this.categories.update(cats => cats.filter(c => c.id !== id));
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to delete category');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Move category to different parent
   */
  moveCategory(id: number, request: MoveCategoryRequest): Observable<CategoryResponse> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.put<CategoryResponse>(`${this.apiUrl}/${id}/move`, request).pipe(
      tap({
        next: () => {
          this.isLoading.set(false);
          // Refresh tree
          this.getCategoryTree().subscribe();
        },
        error: (err) => {
          this.error.set(err.error?.error || 'Failed to move category');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Toggle tree node expansion (UI helper)
   */
  toggleNode(node: CategoryTree): void {
    node.expanded = !node.expanded;
  }

  /**
   * Expand all nodes (UI helper)
   */
  expandAll(nodes: CategoryTree[]): void {
    nodes.forEach(node => {
      node.expanded = true;
      if (node.children && node.children.length > 0) {
        this.expandAll(node.children);
      }
    });
  }

  /**
   * Collapse all nodes (UI helper)
   */
  collapseAll(nodes: CategoryTree[]): void {
    nodes.forEach(node => {
      node.expanded = false;
      if (node.children && node.children.length > 0) {
        this.collapseAll(node.children);
      }
    });
  }

  /**
   * Clear error message
   */
  clearError(): void {
    this.error.set(null);
  }

  /**
   * Clear selected category
   */
  clearSelection(): void {
    this.selectedCategory.set(null);
  }
}
