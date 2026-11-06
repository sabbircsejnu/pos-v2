export type TaxClassification = 'standard' | 'zero_rated' | 'exempt' | 'none';

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
  terminalId?: number;
  terminalName?: string;
  cashSessionId?: number;
  businessDate?: string;
  saleDateTime?: string;
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
  /** Phase 4: the outlet user credited with the sale (independent of the cashier); absent on older sales. */
  salespersonUserId?: number | null;
  salespersonName?: string | null;
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
  terminalId?: number;
  customerId?: number;
  salesDate?: string;
  items: CreateSaleItemDto[];
  discount: number;
  tax: number;
  paymentMethod: string;
  /** Cash handed over for a single-method cash sale; the server computes change. */
  cashTendered?: number;
  /** Deprecated: the server derives the cashier from the authenticated user. */
  cashierId?: number;
  payments?: CreateSalePaymentDto[];
  idempotencyKey?: string;
  /** Eligible outlet user credited with the sale; validated by the server (SALE_SALESPERSON_NOT_ELIGIBLE). */
  salespersonUserId?: number;
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
  /** Display only: the product name without its variant suffix (the variant is shown separately). */
  displayName?: string;
  variantSku: string;
  quantity: number;
  unitPrice: number;
  discountPercent?: number;
  discountAmount: number;
  subtotal: number;
  appliedRuleName?: string;
  primaryImageThumb?: string;
  /** Display only: barcode and variant attributes of the line (the server identifies the line by variantId). */
  barcode?: string;
  variantLabel?: string;
  /** Display only: last known outlet stock when the line was added (the server re-checks stock atomically). */
  knownStock?: number | null;
  /** Effective tax rate in percent as resolved by the server; unset until the line has been priced by the server. */
  taxRate?: number;
  taxClassification?: TaxClassification;
}

export interface PosProduct {
  variantId: number;
  productId: number;
  productName?: string;
  variantName?: string;
  name: string;
  productCode?: string;
  sku: string;
  barcode?: string;
  attributes?: string;
  price: number;
  stockQty: number;
  categoryId?: number;
  categoryName?: string;
  primaryImageThumb?: string;
  /** Present when the product came from a POS lookup, which already returns the tax details. */
  taxRate?: number;
  taxClassification?: TaxClassification;
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
  /** Effective tax rate in percent. */
  taxRate: number;
  taxClassification?: TaxClassification;
  pricesIncludeTax?: boolean;
  appliedRuleName?: string;
  stockQty: number;
  inStock: boolean;
  stockFromCache: boolean;
}

/** Result of GET /api/settings/pos — the Business POS settings plus the caller's discount permissions. */
export interface PosBusinessSettingsDto {
  maxCashSessionDurationHours: number;
  defaultTimeZoneId: string;
  taxEnabled: boolean;
  defaultTaxRate: number;
  taxName: string;
  pricesIncludeTax: boolean;
  /** Manual discount cap in percent of the pre-discount amount; 0 means manual discounts are not enabled. */
  maxManualDiscountPercent: number;
  version: number;
  updatedAt?: string | null;
  isPersisted: boolean;
  canApplyDiscount: boolean;
  canOverrideDiscountLimit: boolean;
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

export interface ReceiptDocumentDto {
  outputMode: string;
  header: {
    showLogo: boolean;
    logoBase64?: string;
    businessName?: string;
    outletName?: string;
    outletAddress?: string;
    phone?: string;
    binOrVatNo?: string;
    headerText?: string;
    concernOf?: string | null;
    branchName?: string | null;
    showroomHotline?: string | null;
    centralHotline?: string | null;
    website?: string | null;
    taxIdentityLine?: string | null;
  };
  saleInfo: {
    invoiceNumber: string;
    salesDateTime: string;
    cashier: string;
    terminal: string;
    customerName: string;
    salesperson?: string | null;
    businessDate?: string | null;
    customerMobile?: string | null;
    /** The sale time in the outlet's time zone, formatted by the server. */
    invoiceDateText?: string | null;
  };
  items: Array<{
    productName: string;
    variantLabel: string;
    quantity: number;
    unitPrice: number;
    discount: number;
    lineTotal: number;
    vatRate?: number | null;
    vatAmount?: number | null;
    printName?: string | null;
    discountPercent?: number | null;
  }>;
  summary: {
    itemCount: number;
    quantity: number;
    subtotal: number;
    discount: number;
    tax: number;
    roundOff: number;
    grandTotal: number;
    paymentMethod: string;
    paidAmount: number;
    change: number;
  };
  footer: {
    thankYouMessage?: string;
    returnPolicy?: string;
    footerText?: string;
    showBarcode: boolean;
    showQr: boolean;
    codeValue?: string;
    generatedAt: string;
    vatLawNotification?: string | null;
    /** Platform-controlled; null when the Business has the footer disabled. */
    poweredByText?: string | null;
  };
  amountInWords?: string | null;
  paymentLines?: Array<{ label: string; amount: number }>;
  /** Every recorded payment; cash rows carry tendered and change. */
  payments?: Array<{ method: string; amount: number; tendered?: number | null; change: number }>;
  /** True when the selling prices already included VAT. */
  taxInclusive?: boolean;
}

export interface ReceiptPrintAttemptDto {
  action: 'Generated' | 'Printed' | 'Reprinted' | 'SavedAsPdf' | 'PrintFailed';
  outputMode?: string;
  message?: string;
}
