export interface VariantLineItemLike {
  variantId: number;
}

export function isVariantAlreadyInItems<T extends VariantLineItemLike>(
  items: T[],
  variantId: number,
  excludeIndex: number = -1
): boolean {
  if (!variantId) {
    return false;
  }

  return items.some((item, idx) => idx !== excludeIndex && item.variantId === variantId);
}

export function findVariantIndexById<T extends VariantLineItemLike>(
  items: T[],
  variantId: number,
  excludeIndex: number = -1
): number {
  if (!variantId) {
    return -1;
  }

  return items.findIndex((item, idx) => idx !== excludeIndex && item.variantId === variantId);
}
