/**
 * Category model
 */
export interface Category {
  id: number;
  name: string;
  description?: string;
  parentCategoryId?: number;
  parentCategoryName?: string;
  imageUrl?: string;
  displayOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  level: number;
  childCount: number;
  productCount: number;
}

/**
 * Category tree node (hierarchical structure)
 */
export interface CategoryTree {
  id: number;
  name: string;
  description?: string;
  parentCategoryId?: number;
  imageUrl?: string;
  displayOrder: number;
  isActive: boolean;
  level: number;
  productCount: number;
  children: CategoryTree[];
  
  // UI state properties
  expanded?: boolean;
  selected?: boolean;
}

/**
 * Create category request
 */
export interface CreateCategoryRequest {
  name: string;
  description?: string;
  parentCategoryId?: number;
  imageUrl?: string;
  displayOrder: number;
  isActive: boolean;
}

/**
 * Update category request
 */
export interface UpdateCategoryRequest {
  name: string;
  description?: string;
  parentCategoryId?: number;
  imageUrl?: string;
  displayOrder: number;
  isActive: boolean;
}

/**
 * Move category request
 */
export interface MoveCategoryRequest {
  newParentCategoryId?: number;
  newDisplayOrder?: number;
}

/**
 * API response wrapper
 */
export interface CategoryResponse {
  data: Category;
  message: string;
}

export interface CategoryListResponse {
  data: Category[];
  count: number;
  message: string;
}

export interface CategoryTreeResponse {
  data: CategoryTree[];
  count: number;
  message: string;
}
