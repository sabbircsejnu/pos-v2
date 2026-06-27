export interface BarcodeVariantSearchItem {
  id: number;
  productId: number;
  productName: string;
  productCode?: string;
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string;
  finalPrice: number;
  costPrice?: number;
  stockQuantity?: number;
  primaryImageThumb?: string;
}

export interface BarcodeTemplateField {
  id: number;
  fieldKey: string;
  isEnabled: boolean;
  sortOrder: number;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  fontSize?: number;
  fontWeight?: string;
  align?: string;
}

export interface UpsertBarcodeTemplateField {
  fieldKey: string;
  isEnabled: boolean;
  sortOrder: number;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  fontSize?: number;
  fontWeight?: string;
  align?: string;
}

export interface BarcodeTemplate {
  id: number;
  name: string;
  templateType: string;
  paperType: string;
  labelWidthMm: number;
  labelHeightMm: number;
  isDefault: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  fields: BarcodeTemplateField[];
}

export interface UpsertBarcodeTemplateRequest {
  name: string;
  templateType: string;
  paperType: string;
  labelWidthMm: number;
  labelHeightMm: number;
  isDefault: boolean;
  isActive: boolean;
  fields: UpsertBarcodeTemplateField[];
}

export interface BarcodePrintQueueItem {
  variantId: number;
  productName: string;
  variantName: string;
  mainProductCode?: string;
  variantSku: string;
  barcodeValue?: string;
  variantAttributes?: string;
  sellingPrice?: number;
  quantityPrinted: number;
}

export interface RecordBarcodePrintHistoryRequest {
  outletId?: number;
  templateId?: number;
  companyName?: string;
  printMode: string;
  labelWidthMm: number;
  labelHeightMm: number;
  sourceModule?: string;
  sourceReferenceType?: string;
  sourceReferenceId?: number;
  items: BarcodePrintQueueItem[];
}

export interface BarcodePrintHistoryItem {
  id: number;
  variantId: number;
  productName: string;
  variantName: string;
  variantSku: string;
  barcodeValue?: string;
  variantAttributes?: string;
  sellingPrice?: number;
  quantityPrinted: number;
}

export interface BarcodePrintHistory {
  id: number;
  templateId?: number;
  templateName?: string;
  outletId?: number;
  outletName?: string;
  printedByUserId?: number;
  printedByUserName?: string;
  printMode: string;
  labelWidthMm: number;
  labelHeightMm: number;
  totalLabels: number;
  sourceModule?: string;
  sourceReferenceType?: string;
  sourceReferenceId?: number;
  printedAt: string;
  items: BarcodePrintHistoryItem[];
}

export interface BarcodePrintHistorySearchRequest {
  templateId?: number;
  printedByUserId?: number;
  startDate?: string;
  endDate?: string;
  pageNumber: number;
  pageSize: number;
}

export interface BarcodePrintHistoryList {
  rows: BarcodePrintHistory[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

export interface LabelSizePreset {
  key: string;
  name: string;
  widthMm: number;
  heightMm: number;
}
