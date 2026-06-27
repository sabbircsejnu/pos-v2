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
  isRoleSwitched?: boolean;
  isBusinessOwner?: boolean;
}

export interface RoleSwitchRequest {
  actingRole: string;
  actingOutletId?: number;
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
