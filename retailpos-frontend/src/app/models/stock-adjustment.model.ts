export interface StockAdjustmentLineDto {
  id: number;
  variantId: number;
  productName: string;
  variantSku: string;
  productCode: string;
  barcode: string;
  previousQuantity: number;
  quantityChange: number;
  newQuantity: number;
  reason: string;
  notes?: string;
}

export interface StockAdjustmentDto {
  id: number;
  adjustmentNumber: string;
  status: StockAdjustmentStatus;
  locationId: number;
  locationType: string;
  locationName: string;
  variantId: number;
  variantSku: string;
  productName: string;
  previousQuantity: number;
  quantityChange: number;
  newQuantity: number;
  reason: string;
  notes?: string;
  lineCount: number;
  totalIncrease: number;
  totalDecrease: number;
  netQuantityChange: number;
  lines: StockAdjustmentLineDto[];
  adjustedBy: number;
  adjusterName: string;
  adjustmentDate: string;
  createdAt: string;
  updatedAt: string;
  submittedAt?: string | null;
  approvedBy?: number | null;
  approvedByName?: string | null;
  approvedAt?: string | null;
  rejectedBy?: number | null;
  rejectedByName?: string | null;
  rejectedAt?: string | null;
  rejectionReason?: string | null;
  cancelledBy?: number | null;
  cancelledByName?: string | null;
  cancelledAt?: string | null;
}

export type StockAdjustmentStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Cancelled';

export interface CreateStockAdjustmentDto {
  action?: StockAdjustmentCreateAction;
  locationId: number;
  locationType: string;
  variantId: number;
  quantityChange: number;
  reason: string;
  notes?: string;
}

export interface CreateStockAdjustmentLineDto {
  variantId: number;
  quantityChange: number;
  reason?: string;
  notes?: string;
}

export interface CreateStockAdjustmentBatchDto {
  action?: StockAdjustmentCreateAction;
  locationId: number;
  locationType: string;
  reason: string;
  notes?: string;
  items: CreateStockAdjustmentLineDto[];
}

export interface UpdateStockAdjustmentDto {
  locationId: number;
  locationType: string;
  items: CreateStockAdjustmentLineDto[];
}

export type StockAdjustmentCreateAction = 'Draft' | 'Submit' | 'SubmitAndApprove';

export interface RejectStockAdjustmentDto {
  reason: string;
}

export interface StockAdjustmentSearchRequest {
  pageNumber: number;
  pageSize: number;
  locationId?: number;
  locationType?: string;
  variantId?: number;
  status?: StockAdjustmentStatus | '';
  startDate?: string;
  endDate?: string;
}

export interface StockAdjustmentListResponse {
  adjustments: StockAdjustmentDto[];
  totalCount: number;
}

export const ADJUSTMENT_REASONS = [
  'Found',
  'Damaged',
  'Expired',
  'Lost',
  'Stolen',
  'OpeningBalanceCorrection',
  'StockCountCorrection',
  'SystemCorrection',
  'Other'
];

export const STOCK_ADJUSTMENT_STATUSES: StockAdjustmentStatus[] = [
  'Draft',
  'PendingApproval',
  'Approved',
  'Rejected',
  'Cancelled'
];

export function getStockAdjustmentStatusColor(status: StockAdjustmentStatus | string): string {
  switch (status) {
    case 'Draft':
      return 'bg-slate-100 text-slate-700';
    case 'PendingApproval':
      return 'bg-amber-100 text-amber-700';
    case 'Approved':
      return 'bg-emerald-100 text-emerald-700';
    case 'Rejected':
      return 'bg-rose-100 text-rose-700';
    case 'Cancelled':
      return 'bg-gray-200 text-gray-600';
    default:
      return 'bg-slate-100 text-slate-700';
  }
}

export function canEditStockAdjustment(status: StockAdjustmentStatus | string): boolean {
  return status === 'Draft';
}

export function canCancelStockAdjustment(status: StockAdjustmentStatus | string): boolean {
  return status === 'Draft' || status === 'PendingApproval';
}
