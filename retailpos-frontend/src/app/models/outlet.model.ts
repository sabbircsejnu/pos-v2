export interface Outlet {
  id: number;
  name: string;
  address: string;
  contactNumber?: string;
  managerId?: number;
  managerName?: string;
  userCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOutletRequest {
  name: string;
  address: string;
  contactNumber?: string;
  managerId?: number;
}

export interface UpdateOutletRequest {
  name: string;
  address: string;
  contactNumber?: string;
  managerId?: number;
}

export interface OutletStats {
  id: number;
  name: string;
  userCount: number;
  salesCount: number;
  totalSales: number;
}
