export interface StockTransferItemDto {
  id: number;
  variantId: number;
  variantSku: string;
  productName: string;
  quantity: number;
}

export interface StockTransferDto {
  id: number;
  fromLocationId: number;
  fromLocationType: string;
  fromLocationName: string;
  toLocationId: number;
  toLocationType: string;
  toLocationName: string;
  transferDate: string;
  status: string;
  approvedBy?: number;
  approverName?: string;
  createdBy?: number;
  creatorName?: string;
  createdAt: string;
  items: StockTransferItemDto[];
}

export interface CreateStockTransferItemDto {
  variantId: number;
  quantity: number;
}

export interface CreateStockTransferDto {
  fromLocationId: number;
  fromLocationType: string;
  toLocationId: number;
  toLocationType: string;
  transferDate: string;
  items: CreateStockTransferItemDto[];
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
    pending: 'Pending',
    approved: 'Approved',
    in_transit: 'In Transit',
    received: 'Received',
    cancelled: 'Cancelled',
    rejected: 'Rejected'
  };
  return labels[status] || status;
}

export function getTransferStatusColor(status: string): string {
  switch (status) {
    case 'pending': return 'bg-yellow-100 text-yellow-800';
    case 'approved': return 'bg-blue-100 text-blue-800';
    case 'in_transit': return 'bg-orange-100 text-orange-800';
    case 'received': return 'bg-green-100 text-green-800';
    case 'cancelled': return 'bg-red-100 text-red-800';
    case 'rejected': return 'bg-red-100 text-red-800';
    default: return 'bg-gray-100 text-gray-800';
  }
}
