export interface Inventory {
  id: number;
  productVariantId: number;
  productName: string;
  variantName: string;
  sku?: string;
  barcode?: string;
  outletId?: number;
  outletName?: string;
  warehouseId?: number;
  warehouseName?: string;
  locationType: string;
  quantity: number;
  reorderLevel: number;
  maxStockLevel?: number;
  batchNumber?: string;
  expiryDate?: string;
  costPrice: number;
  retailPrice: number;
  lastRestockedAt?: string;
  isLowStock: boolean;
  isOutOfStock: boolean;
  isExpiringSoon: boolean;
  totalValue: number;
}

export interface InventoryByLocation {
  outletId?: number;
  outletName?: string;
  warehouseId?: number;
  warehouseName?: string;
  locationType: string;
  totalProducts: number;
  totalQuantity: number;
  lowStockCount: number;
  outOfStockCount: number;
  totalValue: number;
}

export interface LowStock {
  inventoryId: number;
  productVariantId: number;
  productName: string;
  variantName: string;
  sku?: string;
  currentQuantity: number;
  reorderLevel: number;
  shortageQuantity: number;
  locationName: string;
  locationType: string;
  lastRestockedAt?: string;
}

export interface InventoryValuation {
  locationName: string;
  locationType: string;
  totalItems: number;
  totalQuantity: number;
  totalCostValue: number;
  totalRetailValue: number;
  potentialProfit: number;
  profitMarginPercentage: number;
  categoryBreakdown: CategoryValuation[];
}

export interface CategoryValuation {
  categoryName: string;
  productCount: number;
  totalQuantity: number;
  totalCostValue: number;
  totalRetailValue: number;
}

export interface InventorySearchRequest {
  productName?: string;
  sku?: string;
  categoryId?: number;
  outletId?: number;
  warehouseId?: number;
  lowStockOnly?: boolean;
  outOfStockOnly?: boolean;
  expiringSoon?: boolean;
  expiringWithinDays?: number;
  page: number;
  pageSize: number;
}

export interface UpdateStockThreshold {
  reorderLevel: number;
  maxStockLevel?: number;
}
