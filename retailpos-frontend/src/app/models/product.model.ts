// Product Models

export interface Product {
  id: number;
  name: string;
  description?: string;
  /** Business-facing master product identifier. Separate from the variant-level SKU. */
  productCode?: string;
  sku?: string;
  barcode?: string;
  categoryId: number;
  categoryName: string;
  basePrice: number;
  costPrice: number;
  taxRate: number;
  hasVariants: boolean;
  primaryImageThumb?: string;
  primaryImageMedium?: string;
  /** Canonical status: 'active' | 'inactive' | 'draft' */
  status: string;
  /** True when status === 'active'. Convenience alias. */
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
  /** Main product code from the parent product. */
  productCode?: string;
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
  productCode?: string;
  sku?: string;
  barcode?: string;
  categoryId: number;
  basePrice: number;
  costPrice: number;
  taxRate: number;
  hasVariants: boolean;
  /** 'active' | 'inactive' | 'draft' */
  status: string;
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
  productCode?: string;
  sku?: string;
  barcode?: string;
  categoryId: number;
  basePrice: number;
  costPrice: number;
  taxRate: number;
  /** 'active' | 'inactive' | 'draft' */
  status: string;
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
  /** Filter by status. 'active' | 'inactive' | 'draft'. Undefined = all. */
  status?: string;
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
