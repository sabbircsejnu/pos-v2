export interface GrnItem {
  id: number;
  grnId: number;
  poItemId: number;
  productName: string;
  productCode?: string;
  variantName?: string;
  variantAttributes?: string;
  orderedQty: number;
  receivedQty: number;
}

export interface Grn {
  id: number;
  poId: number;
  poNumber: string;
  poOrderNumber?: string;
  supplierName: string;
  warehouseName: string;
  receivedDate: string;
  status: string; // 'full' | 'partial'
  notes?: string;
  createdBy?: number;
  creatorName?: string;
  createdAt: string;
  items: GrnItem[];
}

export interface PurchaseOrderItemForGrn {
  id: number;
  variantId: number;
  productName: string;
  productCode?: string;
  variantName?: string;
  variantAttributes?: string;
  orderedQty: number;
  unitPrice: number;
  primaryImageThumb?: string;
}

export interface PurchaseOrderForGrn {
  id: number;
  orderNumber: string;
  supplierName: string;
  warehouseName: string;
  orderDate: string;
  totalAmount: number;
  items: PurchaseOrderItemForGrn[];
}

export interface GrnVarianceItem {
  poItemId: number;
  productName: string;
  productCode?: string;
  variantName?: string;
  variantAttributes?: string;
  orderedQty: number;
  receivedQty: number;
  variance: number;
}

export interface GrnVariance {
  grnId: number;
  poNumber: string;
  items: GrnVarianceItem[];
}

export interface CreateGrnItemDto {
  poItemId: number;
  receivedQty: number;
}

export interface CreateGrnDto {
  poId: number;
  receivedDate: string;
  items: CreateGrnItemDto[];
}

export interface GrnSearchRequest {
  poId?: number;
  status?: string;
  pageNumber: number;
  pageSize: number;
}

export interface GrnListResponse {
  grns: Grn[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
