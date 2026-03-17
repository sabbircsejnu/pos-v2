export interface User {
  id: number;
  name: string;
  email: string;
  roleId: number | null;
  roleName?: string;
  outletId: number | null;
  outletName?: string;
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
  isActive: boolean;
}

export interface UpdateUserDto {
  name: string;
  email: string;
  roleId: number;
  outletId?: number;
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
