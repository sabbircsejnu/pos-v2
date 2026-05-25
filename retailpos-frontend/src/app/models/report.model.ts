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
  defaultOutletId?: number | null;
  isBusinessOwner: boolean;
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
