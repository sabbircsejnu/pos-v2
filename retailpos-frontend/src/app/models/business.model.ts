export interface OnboardedUserDto {
  userId: number;
  name: string;
  email: string;
  role: string;
  invitationToken: string;
  invitationExpiresAt: string;
}

export interface CreateBusinessRequestDto {
  businessName: string;
  businessEmail?: string;
  businessPhone?: string;
  businessAddress?: string;

  ownerName: string;
  ownerEmail: string;

  outletManagerName: string;
  outletManagerEmail: string;

  salesPersonName: string;
  salesPersonEmail: string;

  accountsAdminName: string;
  accountsAdminEmail: string;

  defaultOutletName: string;

  createDefaultWarehouse: boolean;
  defaultWarehouseName?: string;
  warehouseManagerName?: string;
  warehouseManagerEmail?: string;
}

export interface CreateBusinessResponseDto {
  businessId: number;
  businessName: string;
  defaultOutletId: number;
  defaultWarehouseId?: number;
  users: OnboardedUserDto[];
}

export interface BusinessSummaryDto {
  businessId: number;
  businessName: string;
  businessEmail?: string;
  businessPhone?: string;
  isActive: boolean;
  subscriptionPlan?: string;
  trialEndsAt?: string;
  subscriptionEndsAt?: string;
  maxOutlets?: number;
  maxUsers?: number;
  userCount: number;
  outletCount: number;
  warehouseCount: number;
  businessOwnerEmail?: string;
}

export interface UpdateBusinessStatusDto {
  isActive: boolean;
}

export interface UpdateBusinessSubscriptionDto {
  subscriptionPlan?: string;
  trialEndsAt?: string;
  subscriptionEndsAt?: string;
  maxOutlets?: number;
  maxUsers?: number;
}

export interface BusinessFeatureSettingDto {
  featureKey: string;
  isEnabled: boolean;
  limitValue?: number;
}

export interface UpdateBusinessFeatureSettingsDto {
  features: { featureKey: string; isEnabled: boolean; limitValue?: number }[];
}

export interface ResetOwnerAccessResponseDto {
  userId: number;
  ownerEmail: string;
  invitationToken: string;
  invitationExpiresAt: string;
}

export interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data?: T;
  errors?: Record<string, string[]>;
}
