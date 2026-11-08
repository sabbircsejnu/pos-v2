/**
 * Product Exchange (Exchange SRS / LLD §9). Every amount, VAT figure, date and the exchange number comes from the
 * server; the client only sends identifiers, quantities, choices and the reviewed preview hash.
 */

export interface ExchangeApiResponse<T> {
  success: boolean;
  message?: string;
  data: T;
  errors?: string[] | Record<string, string[]> | null;
  errorCode?: string | null;
}

export type PhoneVerificationMode = 'optional' | 'required';
export type NegativeTargetAdjustmentMode = 'track_only' | 'deduct_from_exchange_salesperson';
export type ExchangeSourceKind = 'sale_item' | 'exchange_replacement_line';
export type ExchangeReasonCode =
  | 'size_fitting'
  | 'colour_design'
  | 'wrong_product_purchased'
  | 'customer_preference_changed'
  | 'other';
export type ExchangeApprovalType = 'window' | 'chain' | 'high_value' | 'refund';
export type ExchangeSettlementDirection = 'collect' | 'refund' | 'none';
export type ExchangeSettlementMethod = 'cash' | 'card' | 'mobile';

export const EXCHANGE_REASONS: ReadonlyArray<{ code: ExchangeReasonCode; label: string }> = [
  { code: 'size_fitting', label: 'Size / Fitting' },
  { code: 'colour_design', label: 'Colour / Design' },
  { code: 'wrong_product_purchased', label: 'Wrong Product Purchased' },
  { code: 'customer_preference_changed', label: 'Customer Preference Changed' },
  { code: 'other', label: 'Other' }
];

export const EXCHANGE_APPROVAL_LABELS: Record<ExchangeApprovalType, string> = {
  window: 'Expired exchange window override',
  chain: 'Replacement chain override',
  high_value: 'High-value exchange approval',
  refund: 'Refund approval'
};

export const EXCHANGE_METHOD_LABELS: Record<ExchangeSettlementMethod, string> = {
  cash: 'Cash',
  card: 'Card',
  mobile: 'Mobile Banking'
};

export function exchangeReasonLabel(code: string | null | undefined): string {
  return EXCHANGE_REASONS.find(reason => reason.code === code)?.label ?? (code ?? '');
}

export function exchangeApprovalLabel(type: string | null | undefined): string {
  return EXCHANGE_APPROVAL_LABELS[type as ExchangeApprovalType] ?? (type ?? '');
}

export function exchangeMethodLabel(method: string | null | undefined): string {
  return EXCHANGE_METHOD_LABELS[method as ExchangeSettlementMethod] ?? (method ?? '');
}

export function settlementDirectionLabel(direction: string | null | undefined): string {
  switch (direction) {
    case 'collect': return 'Collect from customer';
    case 'refund': return 'Refund to customer';
    case 'none': return 'No money moves';
    default: return direction ?? '';
  }
}

// ---- Policy -------------------------------------------------------------------------------------------------------

export interface ExchangePolicy {
  isConfigured: boolean;
  versionNo: number | null;
  isEnabled: boolean;
  allowLowerPriceExchange: boolean;
  exchangeWindowDays: number | null;
  allowReplacementReExchange: boolean;
  replacementWindowDays: number | null;
  maxExchangeCountPerLineage: number | null;
  phoneVerificationMode: PhoneVerificationMode;
  /** Always false today (DN-01): customer phone numbers cannot be verified yet. */
  requiredPhoneVerificationAvailable: boolean;
  highValueApprovalThreshold: number | null;
  negativeTargetAdjustmentMode: NegativeTargetAdjustmentMode;
  updatedAtUtc: string | null;
}

export interface SaveExchangePolicyRequest {
  expectedVersionNo: number | null;
  isEnabled: boolean;
  allowLowerPriceExchange: boolean;
  exchangeWindowDays: number;
  allowReplacementReExchange: boolean;
  replacementWindowDays: number | null;
  maxExchangeCountPerLineage: number | null;
  phoneVerificationMode: PhoneVerificationMode;
  highValueApprovalThreshold: number | null;
  negativeTargetAdjustmentMode: NegativeTargetAdjustmentMode;
}

// ---- Source invoice -----------------------------------------------------------------------------------------------

export interface ExchangeSourceLine {
  saleItemId: number;
  variantId: number;
  productName: string;
  variantName: string;
  sku: string | null;
  barcode: string | null;
  /** Base units. */
  quantity: number;
  returnedQuantity: number;
  remainingQuantity: number;
  unitLabel: string | null;
  enteredQuantity: number | null;
  paidGross: number | null;
  evidenceAvailable: boolean;
  /** 'exchange_replacement_line' when the line is an earlier exchange's replacement; `saleItemId` is then its id (EX-04). */
  sourceKind?: ExchangeSourceKind;
}

/** What a source lookup found: an original sale, or an earlier exchange whose replacements can be re-exchanged (EX-04). */
export type ExchangeSourceDocumentKind = 'sale' | 'exchange';

export interface ExchangeSourceInvoice {
  /** 0 for an exchange source: it is never sent in `sourceSaleIds`. */
  saleId: number;
  kind?: ExchangeSourceDocumentKind;
  invoiceNumber: string;
  purchaseLocalDate: string;
  outletId: number;
  outletName: string | null;
  customerId: number | null;
  customerName: string | null;
  customerPhoneMasked: string | null;
  isWalkIn: boolean;
  status: string;
  eligible: boolean;
  windowExpired: boolean;
  lastEligibleDate: string | null;
  lines: ExchangeSourceLine[];
}

// ---- Preview / confirm --------------------------------------------------------------------------------------------

export interface ExchangeReturnInput {
  sourceKind: ExchangeSourceKind;
  sourceLineId: number;
  quantity: number;
  conditionSellable: boolean;
  packagingReusable: boolean;
}

export interface ExchangeReplacementInput {
  variantId: number;
  quantity: number;
  discountAmount: number;
  sellingOptionId?: number;
  expectedConfigVersionId?: number;
  fulfilmentState?: string;
  includePackaging: boolean;
}

export interface ExchangeRequest {
  receivingOutletId: number;
  terminalId: number | null;
  sourceSaleIds: number[];
  returns: ExchangeReturnInput[];
  replacements: ExchangeReplacementInput[];
  invoiceDiscount: number;
  salespersonUserId?: number | null;
  reasonCode: ExchangeReasonCode;
  reasonOtherText?: string | null;
  internalNote?: string | null;
}

export interface ExchangePreviewReturnLine {
  sourceKind: ExchangeSourceKind;
  sourceLineId: number;
  variantId: number;
  quantity: number;
  creditNet: number;
  creditVat: number;
  creditGross: number;
  anchorDate: string;
  windowExpired: boolean;
  chainExceeded: boolean;
}

export interface ExchangePreviewReplacementLine {
  variantId: number;
  sellingOptionId: number | null;
  baseQuantity: number;
  unitPrice: number;
  lineDiscount: number;
  invoiceDiscountShare: number;
  taxableAmount: number;
  vatRate: number;
  vatAmount: number;
  gross: number;
  availableStock: number;
}

export interface ExchangePreview {
  previewHash: string;
  eligibilityLocalDate: string;
  receivingOutletId: number;
  terminalId: number;
  customerId: number;
  phoneVerificationResult: string;
  returns: ExchangePreviewReturnLine[];
  replacements: ExchangePreviewReplacementLine[];
  returnNet: number;
  returnVat: number;
  returnGross: number;
  replacementNet: number;
  replacementVat: number;
  replacementGross: number;
  replacementDiscount: number;
  netAdjustmentGross: number;
  settlementDirection: ExchangeSettlementDirection;
  settlementAmount: number;
  highValueAmount: number;
  requiredApprovals: ExchangeApprovalType[];
  policyVersionNo: number;
  loyaltyStatus: string;
}

export interface ExchangeApprovalItem {
  type: ExchangeApprovalType;
  reason: string;
}

export interface GrantExchangeApprovalsRequest {
  preview: ExchangeRequest;
  previewHash: string;
  approvals: ExchangeApprovalItem[];
  supervisorLogin: string;
  supervisorPassword: string;
  refundAmount?: number;
  refundMethod?: ExchangeSettlementMethod;
}

export interface ExchangeApprovalToken {
  approvalId: number;
  approvalToken: string;
  type: ExchangeApprovalType;
  expiresAtUtc: string;
  supervisorName: string;
}

export interface ExchangeSettlementInput {
  direction: 'collect' | 'refund';
  method: ExchangeSettlementMethod;
  amount: number;
  tendered?: number | null;
  externalReference?: string | null;
  externalRefundConfirmed?: boolean;
}

export interface ConfirmExchangeRequest extends ExchangeRequest {
  previewHash: string;
  idempotencyKey: string;
  settlements: ExchangeSettlementInput[];
  approvalTokens: string[];
}

export interface ExchangeResult {
  exchangeId: number;
  exchangeNumber: string;
  replayed: boolean;
}

// ---- History ------------------------------------------------------------------------------------------------------

export interface ExchangeListFilters {
  search?: string;
  from?: string;
  to?: string;
  outletId?: number | null;
  status?: string;
  page?: number;
  pageSize?: number;
}

// ---- Metrics (EX-20, D-06, D-07) -----------------------------------------------------------------------------------

export interface ExchangeMetricsFilters {
  from?: string;
  to?: string;
  outletId?: number | null;
}

export interface ExchangeOutletMetrics {
  outletId: number;
  outletName: string | null;
  netAdjustmentNet: number;
  netAdjustmentGross: number;
  exchangeCount: number;
}

export interface ExchangeSalespersonMetrics {
  salespersonUserId: number | null;
  salespersonName: string | null;
  positiveNet: number;
  negativeNet: number;
  signedNet: number;
  targetEffectNet: number;
}

/** Amounts are net of reversals dated in the range; exchanges and reversals count on their own business dates. */
export interface ExchangeMetrics {
  from: string;
  to: string;
  exchangeCount: number;
  reversalCount: number;
  replacementGross: number;
  returnGross: number;
  netAdjustmentGross: number;
  replacementNet: number;
  returnNet: number;
  netAdjustmentNet: number;
  byOutlet: ExchangeOutletMetrics[];
  bySalesperson: ExchangeSalespersonMetrics[];
}

export interface ExchangeListItem {
  id: number;
  exchangeNumber: string;
  businessDate: string;
  completedAtUtc: string;
  receivingOutletId: number;
  receivingOutletName: string | null;
  customerName: string | null;
  originalInvoiceNumbers: string[];
  reasonCode: string;
  netAdjustmentGross: number;
  settlementDirection: string;
  settlementAmount: number;
  status: string;
  cashierName: string | null;
  salespersonName: string | null;
}

export interface ExchangeListPage {
  items: ExchangeListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ExchangeDetailReturn {
  id: number;
  sourceKind: string;
  saleItemId: number | null;
  sourceReplacementLineId: number | null;
  invoiceNumber: string | null;
  variantId: number;
  productName: string;
  variantName: string | null;
  sku: string | null;
  quantity: number;
  unitLabel: string | null;
  creditNet: number;
  creditVat: number;
  creditGross: number;
}

export interface ExchangeDetailReplacement {
  id: number;
  variantId: number;
  productName: string;
  variantName: string | null;
  sku: string | null;
  quantity: number;
  unitLabel: string | null;
  unitPrice: number;
  lineDiscount: number;
  invoiceDiscountShare: number;
  taxableAmount: number;
  vatAmount: number;
  gross: number;
  returnedQuantity: number;
}

export interface ExchangeDetailSettlement {
  direction: string;
  method: string;
  amount: number;
  tendered: number | null;
  changeGiven: number | null;
  referenceLastFour: string | null;
  isReversal: boolean;
}

export interface ExchangeDetailApproval {
  type: string;
  supervisorName: string;
  reason: string;
  createdAtUtc: string;
}

export interface ExchangeReversalSummary {
  id: number;
  reversalNumber: string;
  businessDate: string;
  reason: string;
  actorName: string;
  completedAtUtc: string;
}

export interface ExchangeDetail {
  id: number;
  exchangeNumber: string;
  businessDate: string;
  eligibilityLocalDate: string;
  completedAtUtc: string;
  status: string;
  receivingOutletId: number;
  receivingOutletName: string | null;
  cashierName: string | null;
  salespersonName: string | null;
  customerId: number | null;
  customerName: string | null;
  reasonCode: string;
  reasonOtherText: string | null;
  internalNote: string | null;
  phoneVerificationResult: string | null;
  loyaltyStatus: string | null;
  journalEntryId: number | null;
  returnGross: number;
  returnVat: number;
  replacementGross: number;
  replacementVat: number;
  replacementDiscount: number;
  netAdjustmentGross: number;
  settlementDirection: string;
  settlementAmount: number;
  highValueAmount: number;
  originalInvoices: Array<{ saleId: number; invoiceNumber: string; purchaseLocalDate: string }>;
  returns: ExchangeDetailReturn[];
  replacements: ExchangeDetailReplacement[];
  settlements: ExchangeDetailSettlement[];
  approvals: ExchangeDetailApproval[];
  reversal: ExchangeReversalSummary | null;
}

// ---- Receipt ------------------------------------------------------------------------------------------------------

export interface ExchangeReceiptLine {
  kind: string;
  variantId: number;
  quantity: number;
  unitLabel: string | null;
  enteredQuantity: number | null;
  net: number;
  vat: number;
  gross: number;
  invoiceNumber: string | null;
}

export interface ExchangeReceiptSettlement {
  direction: string;
  method: string;
  amount: number;
  tendered: number | null;
  changeGiven: number | null;
  referenceLastFour: string | null;
}

export interface ExchangeReceiptSnapshot {
  version: number;
  exchangeNumber: string;
  exchangeId: number;
  businessId: number;
  outletId: number;
  businessDate: string;
  completedAtUtc: string;
  cashierUserId: number;
  salespersonUserId: number | null;
  customerId: number | null;
  originalInvoiceNumbers: string[];
  reasonCode: string;
  reasonOtherText: string | null;
  returned: ExchangeReceiptLine[];
  replacements: ExchangeReceiptLine[];
  returnGross: number;
  replacementGross: number;
  returnVat: number;
  replacementVat: number;
  replacementDiscount: number;
  netAdjustmentGross: number;
  settlementDirection: string;
  settlementAmount: number;
  settlements: ExchangeReceiptSettlement[];
  loyaltyStatus: string;
}

export interface ExchangeReceiptDocument {
  document: ExchangeReceiptSnapshot;
  businessName: string | null;
  outletName: string | null;
  cashierName: string | null;
  salespersonName: string | null;
  variantNames: Record<string, string>;
}

export interface ExchangeReceiptPrintAttempt {
  terminalId?: number | null;
  succeeded: boolean;
}

// ---- Reversal -----------------------------------------------------------------------------------------------------

export interface ExchangeReversalEligibility {
  canReverse: boolean;
  blockingReasons: string[];
  settlementDirection: ExchangeSettlementDirection;
  settlementAmount: number;
}

export interface ReverseExchangeRequest {
  reason: string;
  terminalId?: number | null;
  idempotencyKey: string;
  settlements: ExchangeSettlementInput[];
}

export interface ExchangeReversalResult {
  exchangeId: number;
  reversalId: number;
  reversalNumber: string;
  replayed: boolean;
}
