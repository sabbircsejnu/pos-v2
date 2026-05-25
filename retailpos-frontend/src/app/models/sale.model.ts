export interface SaleItemDto {
  id: number;
  variantId: number;
  productName: string;
  variantSku: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  subtotal: number;
  appliedRuleName?: string;
}

// NEW — per-row payment method breakdown
export interface SalePaymentDto {
  id: number;
  method: string;
  amount: number;
  tendered?: number;
  change: number;
}

export interface SaleDto {
  id: number;
  saleNumber: string;
  outletId: number;
  outletName: string;
  customerId?: number;
  customerName?: string;
  saleDate: string;
  totalAmount: number;
  discount: number;
  tax: number;
  netTotal: number;
  paymentMethod: string;
  status: string;
  cashierId: number;
  cashierName: string;
  createdAt: string;
  items: SaleItemDto[];
  payments: SalePaymentDto[];
}

export interface SaleListDto {
  id: number;
  saleNumber: string;
  outletName: string;
  customerName?: string;
  saleDate: string;
  totalAmount: number;
  discount: number;
  tax: number;
  netTotal: number;
  paymentMethod: string;
  status: string;
  cashierName: string;
  itemCount: number;
}

export interface CreateSaleItemDto {
  variantId: number;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  appliedRuleId?: number;
  appliedRuleName?: string;
}

// NEW — one entry per payment method for split-payment checkout
export interface CreateSalePaymentDto {
  method: string;
  amount: number;
  tendered?: number;
}

export interface CreateSaleDto {
  outletId: number;
  customerId?: number;
  items: CreateSaleItemDto[];
  discount: number;
  tax: number;
  paymentMethod: string;
  cashierId: number;
  payments?: CreateSalePaymentDto[];
  idempotencyKey?: string;
}

export interface VoidSaleDto {
  reason: string;
}

export interface SaleSummaryDto {
  totalSales: number;
  totalAmount: number;
  totalDiscount: number;
  totalTax: number;
  paymentBreakdown: { [key: string]: number };
}

export interface CartItem {
  variantId: number;
  productName: string;
  variantSku: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  subtotal: number;
  appliedRuleName?: string;
  primaryImageThumb?: string;
}

export interface PosProduct {
  variantId: number;
  productId: number;
  name: string;
  sku: string;
  price: number;
  stockQty: number;
  categoryId?: number;
  categoryName?: string;
  primaryImageThumb?: string;
}

// NEW — Result of GET /api/pos/lookup (barcode/SKU scan)
export interface PosProductLookupDto {
  variantId: number;
  productId: number;
  productName: string;
  variantName: string;
  barcode?: string;
  sku: string;
  imageUrl?: string;
  basePrice: number;
  effectivePrice: number;
  taxRate: number;
  appliedRuleName?: string;
  stockQty: number;
  inStock: boolean;
  stockFromCache: boolean;
}

// NEW — Result of GET /api/pos/stock-hint
export interface PosStockHintDto {
  variantId: number;
  outletId: number;
  availableQty: number;
  sufficient: boolean;
  fromCache: boolean;
}

// NEW — Hold/park cart DTOs
export interface HeldSaleItemDto {
  variantId: number;
  productName: string;
  variantSku: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  appliedRuleName?: string;
}

export interface HoldSaleDto {
  outletId: number;
  cashierId: number;
  customerId?: number;
  items: HeldSaleItemDto[];
  discountPercent: number;
  paymentMethod: string;
  note?: string;
}

export interface HeldSaleDto {
  id: number;
  outletId: number;
  outletName: string;
  cashierId: number;
  cashierName: string;
  customerId?: number;
  customerName?: string;
  items: HeldSaleItemDto[];
  discountPercent: number;
  paymentMethod: string;
  note?: string;
  heldAt: string;
  subtotal: number;
}
