export interface Warehouse {
  id: number;
  name: string;
  address: string;
  capacity?: number;
  managerId?: number;
  managerName?: string;
  purchaseOrderCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateWarehouseRequest {
  name: string;
  address: string;
  capacity?: number;
  managerId?: number;
}

export interface UpdateWarehouseRequest {
  name: string;
  address: string;
  capacity?: number;
  managerId?: number;
}

export interface WarehouseStats {
  id: number;
  name: string;
  capacity?: number;
  purchaseOrderCount: number;
  currentStock: number;
  capacityUtilization: number;
}
