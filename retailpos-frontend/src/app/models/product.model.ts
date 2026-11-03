// Product Models

/** exempt / zero_rated force 0% tax; null derives the treatment from the rate. */
export type ProductTaxClassification = 'standard' | 'zero_rated' | 'exempt';

export const PRODUCT_TAX_CLASSIFICATIONS: { value: ProductTaxClassification | null; label: string }[] = [
  { value: null, label: 'Automatic (from tax rate)' },
  { value: 'standard', label: 'Standard' },
  { value: 'zero_rated', label: 'Zero-rated' },
  { value: 'exempt', label: 'Exempt' },
];

/** Readable tax treatment: a null rate means "not configured", so the Business default rate applies. */
export function describeProductTax(
  taxRate: number | null | undefined,
  taxClassification: ProductTaxClassification | null | undefined
): string {
  if (taxClassification === 'exempt') return 'Exempt';
  if (taxClassification === 'zero_rated') return '0% (zero-rated)';
  if (taxRate === null || taxRate === undefined) return 'Business default';
  if (taxRate === 0) return '0% (zero-rated)';
  return `${taxRate}%`;
}

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
  /** null = not configured (the Business default rate applies); 0 = explicit zero-rated. */
  taxRate: number | null;
  taxClassification?: ProductTaxClassification | null;
  hasVariants: boolean;
  primaryImageThumb?: string;
  primaryImageMedium?: string;
  /** Canonical status: 'active' | 'inactive' | 'draft' */
  status: string;
  /** True when status === 'active'. Convenience alias. */
  isActive: boolean;
  variantCount: number;
  totalStock: number;
  stockStatus?: 'in_stock' | 'partially_in_stock' | 'out_of_stock' | 'not_initialized' | 'unavailable';
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
  taxRate: number | null;
  taxClassification?: ProductTaxClassification | null;
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
  taxRate: number | null;
  taxClassification?: ProductTaxClassification | null;
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
  outletId?: number;
  warehouseId?: number;
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
