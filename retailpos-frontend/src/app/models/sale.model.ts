export interface SaleItemDto {
  id: number;
  variantId: number;
  productName: string;
  variantSku: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

export interface SaleDto {
  id: number;
  saleNumber: string;
  outletId: number;
  outletName: string;
  customerId?: number;
  customerName?: string;
  saleDate: string;
  totalAmount: number;
  discount: number;
  tax: number;
  netTotal: number;
  paymentMethod: string;
  status: string;
  cashierId: number;
  cashierName: string;
  createdAt: string;
  items: SaleItemDto[];
}

export interface SaleListDto {
  id: number;
  saleNumber: string;
  outletName: string;
  customerName?: string;
  saleDate: string;
  totalAmount: number;
  discount: number;
  tax: number;
  netTotal: number;
  paymentMethod: string;
  status: string;
  cashierName: string;
  itemCount: number;
}

export interface CreateSaleItemDto {
  variantId: number;
  quantity: number;
  unitPrice: number;
}

export interface CreateSaleDto {
  outletId: number;
  customerId?: number;
  items: CreateSaleItemDto[];
  discount: number;
  tax: number;
  paymentMethod: string;
  cashierId: number;
}

export interface VoidSaleDto {
  reason: string;
}

export interface SaleSummaryDto {
  totalSales: number;
  totalAmount: number;
  totalDiscount: number;
  totalTax: number;
  paymentBreakdown: { [key: string]: number };
}

export interface CartItem {
  variantId: number;
  productName: string;
  variantSku: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

export interface PosProduct {
  variantId: number;
  productId: number;
  name: string;
  sku: string;
  price: number;
  stockQty: number;
  categoryId?: number;
  categoryName?: string;
}
