export interface Variation {
  id: number;
  name: string;
  displayOrder: number;
  isActive: boolean;
  options: VariationOption[];
  createdAt: Date;
  updatedAt: Date;
}

export interface VariationOption {
  id: number;
  variationId: number;
  name: string;
  priceAdjustment: number;
  displayOrder: number;
  isActive: boolean;
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateVariationDto {
  name: string;
  displayOrder: number;
  isActive: boolean;
  options: CreateVariationOptionDto[];
}

export interface CreateVariationOptionDto {
  name: string;
  priceAdjustment: number;
  displayOrder: number;
  isActive: boolean;
}

export interface UpdateVariationDto {
  name: string;
  displayOrder: number;
  isActive: boolean;
}

export interface UpdateVariationOptionDto {
  name: string;
  priceAdjustment: number;
  displayOrder: number;
  isActive: boolean;
}
