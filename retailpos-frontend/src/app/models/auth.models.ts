export interface User {
  id: number;
  name: string;
  email: string;
  roleName?: string;
  permissions: string[];
  outletId?: number;
  outletName?: string;
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
