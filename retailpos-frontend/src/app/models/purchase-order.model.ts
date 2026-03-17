export interface PurchaseOrderItem {
  id: number;
  variantId: number;
  productName: string;
  variantName: string;
  variantAttributes?: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface PurchaseOrder {
  id: number;
  supplierId: number;
  supplierName: string;
  warehouseId: number;
  warehouseName: string;
  orderDate: Date;
  expectedDelivery?: Date;
  totalAmount: number;
  status: string; // draft, pending, approved, received, cancelled, rejected
  createdBy?: number;
  createdByName?: string;
  createdAt: Date;
  updatedAt: Date;
  items: PurchaseOrderItem[];
  totalItems: number;
  totalQuantity: number;
}

export interface CreatePurchaseOrderItem {
  variantId: number;
  quantity: number;
  unitPrice: number;
}

export interface CreatePurchaseOrderRequest {
  supplierId: number;
  warehouseId: number;
  orderDate: Date;
  expectedDelivery?: Date;
  status?: string; // defaults to 'draft'
  items: CreatePurchaseOrderItem[];
}

export interface UpdatePurchaseOrderRequest {
  supplierId: number;
  warehouseId: number;
  orderDate: Date;
  expectedDelivery?: Date;
  items: CreatePurchaseOrderItem[];
}

export interface PurchaseOrderSearchRequest {
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
export type POStatus = 'draft' | 'pending' | 'approved' | 'received' | 'cancelled' | 'rejected';

// Helper functions
export function getPOStatusLabel(status: string): string {
  const labels: Record<string, string> = {
    'draft': 'Draft',
    'pending': 'Pending Approval',
    'approved': 'Approved',
    'received': 'Received',
    'cancelled': 'Cancelled',
    'rejected': 'Rejected'
  };
  return labels[status.toLowerCase()] || status;
}

export function getPOStatusColor(status: string): string {
  const colors: Record<string, string> = {
    'draft': 'bg-gray-500',
    'pending': 'bg-yellow-500',
    'approved': 'bg-green-500',
    'received': 'bg-blue-500',
    'cancelled': 'bg-red-500',
    'rejected': 'bg-red-700'
  };
  return colors[status.toLowerCase()] || 'bg-gray-500';
}
