export interface PurchaseOrderItem {
  id: number;
  variantId: number;
  productName: string;
  variantName: string;
  productCode?: string;
  sku?: string;
  variantAttributes?: string;
  quantity: number;
  unit?: string;
  unitPrice: number;
  discount: number;
  tax: number;
  totalPrice: number;
}

export interface PurchaseOrder {
  id: number;
  poNumber: string;
  supplierId: number;
  supplierName: string;
  warehouseId: number;
  warehouseName: string;
  orderDate: Date;
  expectedDelivery?: Date;
  totalAmount: number;
  status: string;
  notes?: string;
  rejectionReason?: string;
  createdBy?: number;
  createdByName?: string;
  createdAt: Date;
  updatedAt: Date;
  latestGrnId?: number;
  latestGrnNumber?: string;
  items: PurchaseOrderItem[];
  totalItems: number;
  totalQuantity: number;
}

export interface CreatePurchaseOrderItem {
  variantId: number;
  quantity: number;
  unitPrice: number;
  discount?: number;
  tax?: number;
  unit?: string;
}

export interface CreatePurchaseOrderRequest {
  supplierId: number;
  warehouseId: number;
  orderDate: Date;
  expectedDelivery?: Date;
  status?: string; // defaults to 'draft'
  notes?: string;
  items: CreatePurchaseOrderItem[];
}

export interface CreatePurchaseAndReceiveRequest extends CreatePurchaseOrderRequest {
  idempotencyKey: string;
}

export interface PurchaseAndReceiveResult {
  purchaseOrder: PurchaseOrder;
  grnId: number;
  grnNumber: string;
  isDuplicateRequest: boolean;
}

export interface UpdatePurchaseOrderRequest {
  supplierId: number;
  warehouseId: number;
  orderDate: Date;
  expectedDelivery?: Date;
  notes?: string;
  items: CreatePurchaseOrderItem[];
}

export interface PurchaseOrderSearchRequest {
  searchQuery?: string;
  status?: string;
  supplierId?: number;
  warehouseId?: number;
  startDate?: Date;
  endDate?: Date;
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortOrder: string;
}

export interface PurchaseOrderListResponse {
  purchaseOrders: PurchaseOrder[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface UpdatePurchaseOrderStatusRequest {
  status: string;
  reason?: string;
}

// Helper type for PO status
export type POStatus =
  | 'draft'
  | 'pending'
  | 'sent_back'
  | 'approved'
  | 'partially_received'
  | 'fully_received'
  | 'completed'
  | 'cancelled'
  | 'rejected';

// Helper functions
export function getPOStatusLabel(status: string): string {
  const labels: Record<string, string> = {
    'draft': 'Draft',
    'pending': 'Pending Approval',
    'sent_back': 'Sent Back',
    'approved': 'Approved',
    'partially_received': 'Partially Received',
    'fully_received': 'Fully Received',
    'completed': 'Completed',
    'cancelled': 'Cancelled',
    'rejected': 'Rejected'
  };
  return labels[status.toLowerCase()] || status;
}

export function getPOStatusColor(status: string): string {
  const colors: Record<string, string> = {
    'draft': 'bg-gray-100 text-gray-700',
    'pending': 'bg-yellow-100 text-yellow-800',
    'sent_back': 'bg-orange-100 text-orange-800',
    'approved': 'bg-green-100 text-green-800',
    'partially_received': 'bg-blue-100 text-blue-800',
    'fully_received': 'bg-teal-100 text-teal-800',
    'completed': 'bg-indigo-100 text-indigo-800',
    'cancelled': 'bg-red-100 text-red-700',
    'rejected': 'bg-red-100 text-red-800'
  };
  return colors[status.toLowerCase()] || 'bg-gray-100 text-gray-700';
}
