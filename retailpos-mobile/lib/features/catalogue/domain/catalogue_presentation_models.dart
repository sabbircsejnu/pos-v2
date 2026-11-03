enum CatalogueSearchField {
  mainProductCode('Main Product Code'),
  productName('Product name'),
  sku('SKU'),
  barcode('Barcode');

  const CatalogueSearchField(this.label);

  final String label;
}

enum CatalogueContentState {
  loading,
  empty,
  unavailable,
  stale,
  error,
  unauthorized,
}

enum BarcodeResolutionState { unknown, ambiguous }

final class CatalogueAttributePresentation {
  const CatalogueAttributePresentation({
    required this.name,
    required this.value,
  });

  final String name;
  final String value;
}

final class CatalogueVariantPresentation {
  const CatalogueVariantPresentation({
    required this.productVariantId,
    required this.barcode,
    this.sku,
    this.attributes = const [],
  });

  final int productVariantId;
  final String? sku;
  final String? barcode;
  final List<CatalogueAttributePresentation> attributes;
}

final class CatalogueProductPresentation {
  const CatalogueProductPresentation({
    required this.productId,
    required this.mainProductCode,
    required this.productName,
    required this.variants,
    this.productBarcode,
  });

  final int productId;
  final String mainProductCode;
  final String productName;
  final String? productBarcode;
  final List<CatalogueVariantPresentation> variants;
}
