export interface SalesReportDto {
  totalRevenue: number;
  totalDiscount: number;
  totalTax: number;
  netRevenue: number;
  totalTransactions: number;
  averageOrderValue: number;
}

export interface TopProductDto {
  variantId: number;
  productName: string;
  sku: string;
  totalQuantity: number;
  totalRevenue: number;
}

export interface SalesByOutletDto {
  outletId: number;
  outletName: string;
  transactionCount: number;
  totalRevenue: number;
}

export interface SalesByPaymentMethodDto {
  paymentMethod: string;
  count: number;
  amount: number;
}

export interface DailySalesTrendDto {
  date: string;
  transactionCount: number;
  revenue: number;
}

export interface StockLevelDto {
  variantId: number;
  productName: string;
  sku: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  quantity: number;
  reorderLevel: number;
  isLowStock: boolean;
  estimatedValue: number;
}

export interface CategoryValuationDto {
  categoryName: string;
  value: number;
  itemCount: number;
}

export interface InventoryValuationDto {
  totalValue: number;
  totalItems: number;
  byCategory: CategoryValuationDto[];
}

export interface SlowMovingItemDto {
  variantId: number;
  productName: string;
  sku: string;
  currentStock: number;
  daysSinceLastSale: number;
  estimatedValue: number;
}

export interface PurchaseSummaryDto {
  totalOrders: number;
  totalAmount: number;
  pendingApprovals: number;
  receivedOrders: number;
}

export interface PurchaseBySupplierDto {
  supplierId: number;
  supplierName: string;
  orderCount: number;
  totalAmount: number;
}

export interface StockTransactionReportRowDto {
  id: number;
  transactionDate: string;
  locationId: number;
  locationType: string;
  locationName: string;
  transactionType: string;
  referenceType: string;
  referenceId: number;
  referenceNo: string;
  variantId: number;
  variantCode?: string;
  variantAttributes?: string;
  quantityIn: number;
  quantityOut: number;
  runningBalance: number;
  remarks?: string;
  createdByName?: string;
}

export interface StockTransactionReportSummaryDto {
  openingStock: number;
  stockIn: number;
  stockOut: number;
  closingStock: number;
  currentStock: number;
}

export interface AuthorizedLocationDto {
  id: number;
  name: string;
  type: 'outlet' | 'warehouse';
}

export interface AuthorizedOutletsDto {
  outlets: AuthorizedLocationDto[];
  warehouses: AuthorizedLocationDto[];
  destinationOutlets?: AuthorizedLocationDto[];
  destinationWarehouses?: AuthorizedLocationDto[];
  defaultOutletId?: number | null;
  isBusinessOwner: boolean;
  isGlobalAccess?: boolean;
  accessScope?: 'assigned_only' | 'specific_locations' | 'all_locations';
  defaultLocationId?: number | null;
  defaultLocationType?: 'outlet' | 'warehouse' | null;
}

export interface StockTransactionReportDto {
  productId: number;
  productName: string;
  productCode?: string;
  level: 'product' | 'variant';
  variantId?: number;
  variantCode?: string;
  variantName?: string;
  variantAttributes?: string;
  sku: string;
  outletId?: number;
  locationType?: string;
  dateFrom?: string;
  dateTo?: string;
  summary: StockTransactionReportSummaryDto;
  rows: StockTransactionReportRowDto[];
}

// ── Current Stock Report ─────────────────────────────────────────────────────
export interface CurrentStockRowDto {
  variantId: number;
  productCode: string;
  barcode?: string;
  productName: string;
  category: string;
  locationId: number;
  locationType: string;
  locationName: string;
  availableQuantity: number;
  reservedQuantity: number;
  reorderLevel: number;
  unitCost: number;
  stockValue: number;
  lastPurchaseDate?: string;
  lastSaleDate?: string;
  stockStatus: 'in-stock' | 'low-stock' | 'out-of-stock';
}

export interface CurrentStockSummaryDto {
  totalProducts: number;
  totalQuantity: number;
  totalStockValue: number;
}

export interface CurrentStockReportDto {
  items: CurrentStockRowDto[];
  totalCount: number;
  summary: CurrentStockSummaryDto;
}

// ── Product Ledger Report ────────────────────────────────────────────────────
export interface ProductLedgerRowDto {
  ledgerId: number;
  transactionDate: string;
  transactionType: string;
  transactionTypeLabel: string;
  referenceNumber: string;
  variantId: number;
  productName: string;
  sku: string;
  locationId: number;
  locationName: string;
  openingQuantity: number;
  stockIn: number;
  stockOut: number;
  closingQuantity: number;
  unitCost: number;
  transactionValue: number;
  performedBy?: string;
  remarks?: string;
}

export interface ProductLedgerSummaryDto {
  openingStock: number;
  totalStockIn: number;
  totalStockOut: number;
  closingStock: number;
  totalTransactionValue: number;
}

export interface ProductLedgerReportDto {
  productId: number;
  productName: string;
  productSku?: string;
  variantId?: number;
  variantName?: string;
  items: ProductLedgerRowDto[];
  totalCount: number;
  summary: ProductLedgerSummaryDto;
}

// ── Stock Movement Report ───────────────────────────────────────────────────

export interface StockMovementRowDto {
  variantId: number;
  productCode: string;
  sku: string;
  productName: string;
  variantName?: string;
  variantAttributes?: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  openingStock: number;
  stockIn: number;
  stockOut: number;
  closingStock: number;
  netMovement: number;
}

export interface StockMovementSummaryDto {
  totalOpeningStock: number;
  totalStockIn: number;
  totalStockOut: number;
  totalClosingStock: number;
  netMovement: number;
}

export interface StockMovementReportDto {
  items: StockMovementRowDto[];
  totalCount: number;
  summary: StockMovementSummaryDto;
}

// ── Stock Valuation Report ──────────────────────────────────────────────────

export interface StockValuationRowDto {
  variantId: number;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  quantity: number;
  unitCost: number;
  inventoryValue: number;
  percentOfTotal: number;
}

export interface StockValuationCategoryDto {
  categoryName: string;
  quantity: number;
  inventoryValue: number;
  percentOfTotal: number;
}

export interface StockValuationSummaryDto {
  totalProducts: number;
  totalQuantity: number;
  totalInventoryValue: number;
  byCategory: StockValuationCategoryDto[];
}

export interface StockValuationReportDto {
  items: StockValuationRowDto[];
  totalCount: number;
  summary: StockValuationSummaryDto;
}

// ── Outlet Wise Stock Report ────────────────────────────────────────────────

export interface OutletWiseStockRowDto {
  outletId: number;
  outletName: string;
  variantId: number;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  quantity: number;
  unitCost: number;
  stockValue: number;
}

export interface OutletStockSummaryDto {
  outletId: number;
  outletName: string;
  totalSkus: number;
  totalQty: number;
  stockValue: number;
}

export interface OutletWiseStockSummaryDto {
  totalOutlets: number;
  totalSkus: number;
  totalQuantity: number;
  totalStockValue: number;
  byOutlet: OutletStockSummaryDto[];
}

export interface OutletWiseStockReportDto {
  items: OutletWiseStockRowDto[];
  totalCount: number;
  summary: OutletWiseStockSummaryDto;
}

// ── Report #6: Low Stock ─────────────────────────────────────────────────────
export interface LowStockRowDto {
  variantId: number;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  currentStock: number;
  reorderLevel: number;
  suggestedOrderQty: number;
  urgencyLevel: string;
}
export interface LowStockSummaryDto {
  totalSkus: number;
  totalDeficitQty: number;
  criticalCount: number;
  lowCount: number;
}
export interface LowStockReportDto {
  items: LowStockRowDto[];
  totalCount: number;
  summary: LowStockSummaryDto;
}

// ── Report #7: Out Of Stock ──────────────────────────────────────────────────
export interface OutOfStockRowDto {
  variantId: number;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  reorderLevel: number;
  unitCost: number;
}
export interface OutOfStockSummaryDto {
  totalSkus: number;
  totalLocations: number;
  estimatedCostImpact: number;
}
export interface OutOfStockReportDto {
  items: OutOfStockRowDto[];
  totalCount: number;
  summary: OutOfStockSummaryDto;
}

// ── Report #8: Negative Stock ────────────────────────────────────────────────
export interface NegativeStockRowDto {
  variantId: number;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  currentStock: number;
  unitCost: number;
  stockValue: number;
}
export interface NegativeStockSummaryDto {
  totalSkus: number;
  totalNegativeQty: number;
  totalNegativeValue: number;
}
export interface NegativeStockReportDto {
  items: NegativeStockRowDto[];
  totalCount: number;
  summary: NegativeStockSummaryDto;
}

// ── Report #9: Stock Adjustment ──────────────────────────────────────────────
export interface StockAdjustmentRowDto {
  id: number;
  adjustmentDate: string;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  locationId: number;
  locationType: string;
  locationName: string;
  adjustmentType: string;
  quantityChange: number;
  reason: string;
  adjustedBy: string;
}
export interface StockAdjustmentSummaryDto {
  totalAdjustments: number;
  totalAdditions: number;
  totalReductions: number;
  netQuantityChange: number;
}
export interface StockAdjustmentReportDto {
  items: StockAdjustmentRowDto[];
  totalCount: number;
  summary: StockAdjustmentSummaryDto;
}

// ── Report #10: Stock Transfer ───────────────────────────────────────────────
export interface StockTransferRowDto {
  transferId: number;
  transferDate: string;
  fromLocationName: string;
  fromLocationType: string;
  toLocationName: string;
  toLocationType: string;
  status: string;
  productCode: string;
  barcode: string;
  sku: string;
  productName: string;
  categoryName: string;
  quantity: number;
  unitCost: number;
  transferValue: number;
  createdBy: string;
}
export interface StockTransferSummaryDto {
  totalTransfers: number;
  totalLines: number;
  totalQuantity: number;
  totalValue: number;
  pendingCount: number;
  completedCount: number;
}
export interface StockTransferReportDto {
  items: StockTransferRowDto[];
  totalCount: number;
  summary: StockTransferSummaryDto;
}
