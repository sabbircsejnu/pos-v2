export interface StockTransferItemDto {
  id: number;
  variantId: number;
  variantSku: string;
  productName: string;
  quantity: number;
  requestedQuantity: number;
  transferQuantity: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  unitCost: number;
  remarks?: string;
}

export interface StockTransferDto {
  id: number;
  transferNo?: string;
  transferType: string;
  relatedRequisitionId?: number;
  fromLocationId: number;
  fromLocationType: string;
  fromLocationName: string;
  toLocationId: number;
  toLocationType: string;
  toLocationName: string;
  transferDate: string;
  status: string;
  notes?: string;
  approvedBy?: number;
  approverName?: string;
  approvedAt?: string;
  submittedBy?: number;
  submittedAt?: string;
  dispatchedBy?: number;
  dispatchedAt?: string;
  receivedBy?: number;
  receivedAt?: string;
  rejectedBy?: number;
  rejectedAt?: string;
  cancelledBy?: number;
  cancelledAt?: string;
  updatedBy?: number;
  updatedAt?: string;
  createdBy?: number;
  creatorName?: string;
  createdAt: string;
  items: StockTransferItemDto[];
}

export interface CreateStockTransferItemDto {
  variantId: number;
  quantity: number;
  requestedQuantity?: number;
  transferQuantity?: number;
  unitCost: number;
  remarks?: string;
}

export interface CreateStockTransferDto {
  transferType: string;
  relatedRequisitionId?: number;
  fromLocationId: number;
  fromLocationType: string;
  toLocationId: number;
  toLocationType: string;
  transferDate: string;
  notes?: string;
  items: CreateStockTransferItemDto[];
}

export interface ReceiveStockTransferItemDto {
  variantId: number;
  acceptedQuantity: number;
  rejectedQuantity: number;
  remarks?: string;
}

export interface ReceiveStockTransferDto {
  items: ReceiveStockTransferItemDto[];
  notes?: string;
}

export interface TransferStatusUpdateDto {
  reason?: string;
}

export interface StockTransferSearchRequest {
  status?: string;
  pageNumber: number;
  pageSize: number;
}

export interface StockTransferListResponse {
  transfers: StockTransferDto[];
  totalCount: number;
}

export function getTransferStatusLabel(status: string): string {
  const labels: Record<string, string> = {
    draft: 'Draft',
    submitted: 'Submitted',
    pending: 'Pending',
    approved: 'Approved',
    in_transit: 'In Transit',
    received: 'Received',
    partially_received: 'Partially Received',
    cancelled: 'Cancelled',
    rejected: 'Rejected'
  };
  return labels[status] || status;
}

export function getTransferStatusColor(status: string): string {
  switch (status) {
    case 'draft': return 'bg-gray-100 text-gray-800';
    case 'submitted': return 'bg-yellow-100 text-yellow-800';
    case 'pending': return 'bg-yellow-100 text-yellow-800';
    case 'approved': return 'bg-blue-100 text-blue-800';
    case 'in_transit': return 'bg-orange-100 text-orange-800';
    case 'received': return 'bg-green-100 text-green-800';
    case 'partially_received': return 'bg-amber-100 text-amber-800';
    case 'cancelled': return 'bg-red-100 text-red-800';
    case 'rejected': return 'bg-red-100 text-red-800';
    default: return 'bg-gray-100 text-gray-800';
  }
}

export function getTransferTypeLabel(type: string): string {
  const labels: Record<string, string> = {
    direct: 'Direct',
    requisition: 'Requisition',
    return: 'Return'
  };
  return labels[type] || type;
}
