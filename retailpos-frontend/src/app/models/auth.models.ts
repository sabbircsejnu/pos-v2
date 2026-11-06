export interface User {
  id: number;
  businessId?: number;
  businessName?: string;
  name: string;
  email: string;
  roleName?: string;
  permissions: string[];
  outletId?: number;
  outletName?: string;

  // Session role-switch state
  realRoleName?: string;
  actingRoleName?: string;
  actingOutletId?: number;
  actingOutletName?: string;
  actingLocationType?: ActingLocationType | null;
  actingWarehouseId?: number;
  actingWarehouseName?: string;
  isRoleSwitched?: boolean;
  isBusinessOwner?: boolean;
}

export type ActingLocationType = 'outlet' | 'warehouse' | 'all';

export interface RoleSwitchRequest {
  actingRole: string;
  actingOutletId?: number;
  actingLocationType?: ActingLocationType;
  actingLocationId?: number;
}

export interface UserContextOption {
  id: number;
  name: string;
}

/** Filtered by the server to what the caller may switch to. */
export interface UserContextOptions {
  canSwitch: boolean;
  allowAllLocations: boolean;
  roles: UserContextOption[];
  outlets: UserContextOption[];
  warehouses: UserContextOption[];
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  expiresAt: string;
  user: User;
}

export interface ApiResponse<T> {
  data?: T;
  message?: string;
  errors?: { [key: string]: string[] };
}
