export interface Role {
  id: number;
  name: string;
  permissions: string[];
  userCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateRoleDto {
  name: string;
  permissions: string[];
}

export interface UpdateRoleDto {
  name: string;
  permissions: string[];
}
