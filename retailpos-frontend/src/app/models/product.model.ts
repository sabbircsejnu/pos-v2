// Product Models

export interface Product {
  id: number;
  name: string;
  description?: string;
  sku?: string;
  barcode?: string;
  categoryId: number;
  categoryName: string;
  basePrice: number;
  costPrice: number;
  taxRate: number;
  hasVariants: boolean;
  imageUrl?: string;
  isActive: boolean;
  variantCount: number;
  totalStock: number;
  variants: ProductVariant[];
  createdAt: Date;
  updatedAt: Date;
}

export interface ProductVariant {
  id: number;
  productId: number;
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string; // JSON string
  priceAdjustment: number;
  finalPrice: number;
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateProductRequest {
  name: string;
  description?: string;
  sku?: string;
  barcode?: string;
  categoryId: number;
  basePrice: number;
  costPrice: number;
  taxRate: number;
  hasVariants: boolean;
  imageUrl?: string;
  isActive: boolean;
  variants?: CreateProductVariantRequest[];
}

export interface CreateProductVariantRequest {
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string; // JSON string
  priceAdjustment: number;
}

export interface UpdateProductRequest {
  name: string;
  description?: string;
  sku?: string;
  barcode?: string;
  categoryId: number;
  basePrice: number;
  costPrice: number;
  taxRate: number;
  imageUrl?: string;
  isActive: boolean;
}

export interface UpdateProductVariantRequest {
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string; // JSON string
  priceAdjustment: number;
}

export interface ProductSearchRequest {
  searchQuery?: string;
  categoryId?: number;
  isActive?: boolean;
  hasVariants?: boolean;
  minPrice?: number;
  maxPrice?: number;
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortOrder: string;
}

export interface ProductListResponse {
  products: Product[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

// Helper interfaces for variant attributes
export interface VariantAttribute {
  name: string;
  value: string;
}
