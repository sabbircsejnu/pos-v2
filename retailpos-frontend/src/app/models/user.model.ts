export interface UserWarehouseAssignment {
  id: number;
  warehouseId: number;
  warehouseName: string;
  isPrimary: boolean;
  isActive: boolean;
}

export type InventoryLocationAccessScope = 'assigned_only' | 'specific_locations' | 'all_locations';

export interface UserOutletAssignment {
  id: number;
  outletId: number;
  outletName: string;
  isPrimary: boolean;
  isActive: boolean;
}

export interface User {
  id: number;
  name: string;
  email: string;
  roleId: number | null;
  roleName?: string;
  outletId: number | null;
  outletName?: string;
  businessId?: number;
  businessName?: string;
  inventoryLocationAccessScope?: InventoryLocationAccessScope;
  defaultLocationType?: 'outlet' | 'warehouse' | null;
  defaultLocationId?: number | null;
  outletAssignments?: UserOutletAssignment[];
  warehouseAssignments?: UserWarehouseAssignment[];
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateUserDto {
  name: string;
  email: string;
  password: string;
  roleId: number;
  outletId?: number;
  outletIds?: number[];
  businessId?: number;
  warehouseIds?: number[];
  defaultLocationType?: 'outlet' | 'warehouse';
  defaultLocationId?: number;
  inventoryLocationAccessScope: InventoryLocationAccessScope;
  isActive: boolean;
}

export interface UpdateUserDto {
  name: string;
  email: string;
  roleId: number;
  businessId?: number;
  outletId?: number;
  outletIds?: number[];
  warehouseIds?: number[];
  defaultLocationType?: 'outlet' | 'warehouse';
  defaultLocationId?: number;
  inventoryLocationAccessScope: InventoryLocationAccessScope;
  isActive: boolean;
}

export interface ChangePasswordDto {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface UserListResponse {
  users: User[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
