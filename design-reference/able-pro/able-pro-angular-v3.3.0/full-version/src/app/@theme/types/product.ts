export interface productResponse {
  products: Products[];
}

export type Products = {
  id: string | number | undefined;
  image: string;
  name: string;
  brand: string;
  offer?: string;
  description?: string;
  about?: string;
  quantity: number;
  rating?: number;
  discount?: number;
  salePrice?: number;
  offerPrice?: number;
  gender?: string;
  categories?: string[];
  colors?: string[];
  popularity?: number;
  date?: number;
  created: Date;
  isStock?: boolean;
  new?: number;
  isWhitelisted: string;
};
