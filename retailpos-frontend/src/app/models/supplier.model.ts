export interface Supplier {
  id: number;
  name: string;
  contact?: string;
  address?: string;
  creditLimit: number;
  createdAt: Date;
  updatedAt: Date;
  totalPurchaseOrders?: number;
  totalBills?: number;
  totalPurchaseAmount?: number;
  outstandingBalance?: number;
}

export interface CreateSupplierDto {
  name: string;
  contact?: string;
  address?: string;
  creditLimit: number;
}

export interface UpdateSupplierDto {
  name: string;
  contact?: string;
  address?: string;
  creditLimit: number;
}

export interface SupplierPerformanceDto {
  id: number;
  name: string;
  totalPurchaseOrders: number;
  totalBills: number;
  totalPurchaseAmount: number;
  outstandingBalance: number;
  creditLimit: number;
  creditUtilizationPercentage: number;
  healthStatus: 'Good' | 'Warning' | 'Critical';
}

export interface SupplierSearchRequest {
  searchQuery?: string;
  minCreditLimit?: number;
  maxCreditLimit?: number;
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortOrder: string;
}

export interface SupplierListResponse {
  suppliers: Supplier[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
