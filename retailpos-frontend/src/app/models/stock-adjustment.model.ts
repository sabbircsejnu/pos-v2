export interface StockAdjustmentDto {
  id: number;
  locationId: number;
  locationType: string;
  locationName: string;
  variantId: number;
  variantSku: string;
  productName: string;
  quantityChange: number;
  reason: string;
  notes?: string;
  adjustedBy: number;
  adjusterName: string;
  adjustmentDate: string;
}

export interface CreateStockAdjustmentDto {
  locationId: number;
  locationType: string;
  variantId: number;
  quantityChange: number;
  reason: string;
  notes?: string;
}

export interface StockAdjustmentSearchRequest {
  pageNumber: number;
  pageSize: number;
  locationId?: number;
  locationType?: string;
  variantId?: number;
}

export interface StockAdjustmentListResponse {
  adjustments: StockAdjustmentDto[];
  totalCount: number;
}

export const ADJUSTMENT_REASONS = [
  'Damage',
  'Loss',
  'Found',
  'Expired',
  'Count',
  'Return',
  'Other'
];
