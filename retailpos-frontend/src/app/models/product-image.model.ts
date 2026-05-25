export interface ProductImage {
  id: number;
  productId: number;
  originalName: string;
  mimeType: string;
  size: number;
  width: number;
  height: number;
  thumbUrl: string;
  mediumUrl: string;
  originalUrl: string;
  isPrimary: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

export const PRODUCT_IMAGE_RULES = {
  minDim: 800,
  maxDim: 1200,
  maxBytes: 5 * 1024 * 1024,
  allowedMimes: ['image/jpeg', 'image/jpg', 'image/png', 'image/webp'] as string[],
  allowedExtensions: ['.jpg', '.jpeg', '.png', '.webp'] as string[],
};
