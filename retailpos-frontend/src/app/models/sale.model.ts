import { PackStateKind, ReturnCondition } from './uom.model';

export type TaxClassification = 'standard' | 'zero_rated' | 'exempt' | 'none';

/** Unit fields of a cart, sale or held line. Absent on a plain pcs line. */
export interface SaleLineUomFields {
  sellingOptionId?: number;
  expectedConfigVersionId?: number;
  fulfilmentState?: PackStateKind;
  includePackaging?: boolean;
}

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
  /** Unit snapshot. `quantity` is base and `unitPrice` is per base; a line with no snapshot is legacy pcs. */
  unitCode?: string | null;
  unitLabel?: string | null;
  enteredQuantity?: number | null;
  enteredUnitPrice?: number | null;
  baseUnitCode?: string | null;
  packState?: PackStateKind | null;
  /** Quantity already returned over every refund, in the stored base unit (0 when none). */
  returnedQuantity?: number | null;
  /** Quantity that can still be returned, in the stored base unit; 0 when the sale is closed. */
  remainingReturnableQuantity?: number | null;
}

/** Sale statuses sent by the API. */
export type SaleStatus = 'completed' | 'partially_refunded' | 'refunded' | 'voided';

const SALE_STATUS_LABELS: Readonly<Record<string, string>> = {
  completed: 'Completed',
  partially_refunded: 'Partially refunded',
  refunded: 'Refunded',
  voided: 'Voided',
  void: 'Void',
};

/** A readable label for a sale status; unknown values are shown as sent. */
export function saleStatusLabel(status: string | null | undefined): string {
  const key = (status ?? '').toLowerCase();
  return SALE_STATUS_LABELS[key] ?? (status ?? '');
}

/** One returned line of a recorded refund. Quantities are in the line's stored base unit. */
export interface SaleRefundLineDto {
  saleItemId: number;
  quantity: number;
  condition: ReturnCondition | string;
  restockedQuantity: number;
  packagingReusable: boolean;
  netAmount?: number | null;
  vatAmount?: number | null;
}

export interface SaleRefundPaymentDto {
  method: string;
  amount: number;
}

/** One recorded refund of a sale. Amounts are computed by the server from the original sale. */
export interface SaleRefundDto {
  id: number;
  refundNumber: string;
  sequence: number;
  createdAtUtc: string;
  reason?: string | null;
  actorUserId: number;
  actorName?: string | null;
  netAmount: number;
  vatAmount: number;
  totalAmount: number;
  cashAmount: number;
  lines: SaleRefundLineDto[];
  payments: SaleRefundPaymentDto[];
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
  /** Recorded refunds, oldest first. Sales refunded before refund history was kept have none. */
  refunds?: SaleRefundDto[];
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

export interface CreateSaleItemDto extends SaleLineUomFields {
  variantId: number;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  appliedRuleId?: number;
  appliedRuleName?: string;
  /** The line amount the cashier saw for a pack or custom option (after automatic rules, before manual discount). */
  expectedLineGross?: number;
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

export interface RefundSaleItemDto {
  variantId: number;
  /** Base units. */
  quantity: number;
  saleItemId?: number;
  condition?: ReturnCondition;
  packagingReusable?: boolean;
}

export interface RefundSaleDto {
  items: RefundSaleItemDto[];
  reason?: string;
  terminalId?: number;
  /**
   * The cash actually paid back. Normally omitted: the server works out the cash share. Sent only after the server
   * answered CASH_REFUND_POLICY_UNDEFINED for an old sale without line allocations.
   */
  cashRefundAmount?: number;
  /** One value per refund attempt (max 120 chars), repeated on a retry. Required from a sale's second refund on. */
  idempotencyKey?: string;
}

export interface SaleSummaryDto {
  totalSales: number;
  totalAmount: number;
  totalDiscount: number;
  totalTax: number;
  paymentBreakdown: { [key: string]: number };
}

export interface CartItem extends SaleLineUomFields {
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
  /** The selling option's label. With an option, `quantity` is a pack count or a measured quantity of its unit. */
  unitLabel?: string;
  /**
   * Option lines only: the whole line amount from the server (after automatic rules, before manual discount and
   * tax-exclusive VAT). Unset until the line is priced, and cleared when its quantity or option changes.
   */
  lineGross?: number;
  /** Option lines only: rules that match but cannot be applied to a measured quantity. */
  rulesNotApplied?: string[];
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
export interface HeldSaleItemDto extends SaleLineUomFields {
  unitLabel?: string;
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
