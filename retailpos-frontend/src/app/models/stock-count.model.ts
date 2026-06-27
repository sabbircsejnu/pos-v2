export type StockCountStatus =
  | 'Draft'
  | 'Submitted'
  | 'Rejected'
  | 'Approved'
  | 'AdjustmentGenerated'
  | 'Completed';

export interface StockCountLineDto {
  id: number;
  productId: number;
  variantId: number;
  productName: string;
  productCode: string;
  variantName: string;
  currentStock: number;
  physicalStock?: number | null;
  difference?: number | null;
  remarks?: string | null;
}

export interface StockCountDto {
  id: number;
  stockCountNo: string;
  businessId: number;
  locationId: number;
  locationType: 'outlet' | 'warehouse';
  locationName: string;
  stockCountDate: string;
  status: StockCountStatus;
  remarks?: string | null;
  totalItems: number;
  createdBy: number;
  createdByName: string;
  createdAt: string;
  submittedBy?: number | null;
  submittedAt?: string | null;
  approvedBy?: number | null;
  approvedAt?: string | null;
  rejectedBy?: number | null;
  rejectedAt?: string | null;
  rejectionReason?: string | null;
  hasPostGenerationMovements: boolean;
  postGenerationMovementCount: number;
  lastPostGenerationMovementAt?: string | null;
  lines: StockCountLineDto[];
}

export interface StockCountListDto {
  stockCounts: StockCountDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

export interface StockCountSearchRequest {
  search?: string;
  locationId?: number;
  locationType?: string;
  dateFrom?: string;
  dateTo?: string;
  status?: StockCountStatus;
  pageNumber: number;
  pageSize: number;
}

export interface CreateStockCountRequest {
  stockCountDate: string;
  locationId?: number;
  locationType?: 'outlet' | 'warehouse';
  remarks?: string;
}

export interface StockCountPrintDto {
  id: number;
  stockCountNo: string;
  companyName: string;
  locationName: string;
  locationType: 'outlet' | 'warehouse';
  stockCountDate: string;
  generatedBy: string;
  generatedAt: string;
  hasPostGenerationMovements: boolean;
  postGenerationMovementCount: number;
  lastPostGenerationMovementAt?: string | null;
  lines: StockCountLineDto[];
}

export const STOCK_COUNT_STATUSES: StockCountStatus[] = [
  'Draft',
  'Submitted',
  'Rejected',
  'Approved',
  'AdjustmentGenerated',
  'Completed'
];
