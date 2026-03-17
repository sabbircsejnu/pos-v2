export interface CustomerDto {
  id: number;
  name: string;
  phone?: string;
  email?: string;
  loyaltyPoints: number;
  createdAt: string;
  totalPurchases?: number;
}

export interface CreateCustomerDto {
  name: string;
  phone?: string;
  email?: string;
}

export interface UpdateCustomerDto {
  name: string;
  phone?: string;
  email?: string;
}

export interface CustomerListResponse {
  customers: CustomerDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
