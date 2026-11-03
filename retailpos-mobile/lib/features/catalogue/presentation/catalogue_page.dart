import 'package:flutter/material.dart';
import 'package:iranimart_business_mobile/features/catalogue/domain/catalogue_presentation_models.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

final class CataloguePage extends StatefulWidget {
  const CataloguePage({
    required this.businessId,
    required this.outletName,
    super.key,
  });

  final int businessId;
  final String? outletName;

  @override
  State<CataloguePage> createState() => _CataloguePageState();
}

final class _CataloguePageState extends State<CataloguePage> {
  CatalogueSearchField _searchField = CatalogueSearchField.mainProductCode;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
    children: [
      Text(
        'Product catalogue',
        style: Theme.of(context).textTheme.headlineSmall,
      ),
      const SizedBox(height: 12),
      _ScopeCard(businessId: widget.businessId, outletName: widget.outletName),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.preview,
        title: 'Catalogue not connected',
        message: 'Product and variant data is unavailable. No catalogue request, lookup or synchronization was made.',
      ),
      const SizedBox(height: 20),
      Text('Find a product', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      Text(
        'Search is presentation-only until an authorized catalogue contract is available.',
        style: Theme.of(context).textTheme.bodyMedium,
      ),
      const SizedBox(height: 12),
      DropdownButtonFormField<CatalogueSearchField>(
        initialValue: _searchField,
        isExpanded: true,
        decoration: const InputDecoration(
          labelText: 'Search by',
          border: OutlineInputBorder(),
        ),
        items: [
          for (final field in CatalogueSearchField.values)
            DropdownMenuItem(value: field, child: Text(field.label)),
        ],
        onChanged: (field) {
          if (field != null) setState(() => _searchField = field);
        },
      ),
      const SizedBox(height: 12),
      TextField(
        key: const ValueKey('catalogue-search-query'),
        decoration: InputDecoration(
          labelText: _searchField.label,
          prefixIcon: const Icon(Icons.search),
          border: const OutlineInputBorder(),
        ),
      ),
      const SizedBox(height: 20),
      Text('Filters', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      Text(
        'Filter values come from the authorized catalogue; none are available offline in this preview.',
        style: Theme.of(context).textTheme.bodyMedium,
      ),
      const SizedBox(height: 12),
      const _FilterField(label: 'Category'),
      const SizedBox(height: 12),
      const _FilterField(label: 'Size'),
      const SizedBox(height: 12),
      const _FilterField(label: 'Colour'),
      const SizedBox(height: 12),
      const _FilterField(label: 'Attribute name'),
      const SizedBox(height: 12),
      const _FilterField(label: 'Attribute value'),
      const SizedBox(height: 20),
      Text(
        'Products and variants',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const CatalogueStatusPanel(state: CatalogueContentState.unavailable),
      const SizedBox(height: 20),
      Text('Product details', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      const ProductDetailsPanel(),
    ],
  );
}

final class _ScopeCard extends StatelessWidget {
  const _ScopeCard({required this.businessId, required this.outletName});

  final int businessId;
  final String? outletName;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Signed-in business',
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 4),
          Text('Business $businessId'),
          if (outletName != null) ...[
            const SizedBox(height: 12),
            Text(
              'Selected authorized outlet',
              style: Theme.of(context).textTheme.labelLarge,
            ),
            const SizedBox(height: 4),
            Text(outletName!),
          ],
          const SizedBox(height: 8),
          Text(
            'Displayed session context is not an authorization grant; the ERP must authorize catalogue access.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    ),
  );
}

final class _FilterField extends StatelessWidget {
  const _FilterField({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) => TextField(
    key: ValueKey(
      'catalogue-filter-${label.toLowerCase().replaceAll(' ', '-')}',
    ),
    decoration: InputDecoration(
      labelText: label,
      border: const OutlineInputBorder(),
    ),
  );
}

final class CatalogueStatusPanel extends StatelessWidget {
  const CatalogueStatusPanel({required this.state, super.key});

  final CatalogueContentState state;

  @override
  Widget build(BuildContext context) {
    final (panelState, title, message) = switch (state) {
      CatalogueContentState.loading => (
        ContentState.loading,
        'Loading catalogue',
        'Waiting for an authorized catalogue.',
      ),
      CatalogueContentState.empty => (
        ContentState.empty,
        'No products found',
        'No products match the available catalogue and filters.',
      ),
      CatalogueContentState.unavailable => (
        ContentState.unavailable,
        'Catalogue unavailable',
        'Product data is not available on this device.',
      ),
      CatalogueContentState.stale => (
        ContentState.stale,
        'Catalogue may be out of date',
        'The displayed catalogue revision needs verification before use.',
      ),
      CatalogueContentState.error => (
        ContentState.error,
        'Catalogue could not be loaded',
        'An authorized catalogue response is unavailable. Retry when service is restored.',
      ),
      CatalogueContentState.unauthorized => (
        ContentState.unauthorized,
        'Catalogue access not authorized',
        'The ERP did not authorize this catalogue scope.',
      ),
    };
    return ContentStatePanel(state: panelState, title: title, message: message);
  }
}

final class ProductDetailsPanel extends StatelessWidget {
  const ProductDetailsPanel({this.product, super.key});

  final CatalogueProductPresentation? product;

  @override
  Widget build(BuildContext context) {
    final selectedProduct = product;
    if (selectedProduct == null) {
      return const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Product details unavailable',
        message: 'Select a product from an authorized catalogue when catalogue data is connected.',
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _IdentityRow(
                  label: 'Product ID',
                  value: '${selectedProduct.productId}',
                ),
                _IdentityRow(
                  label: 'Main Product Code',
                  value: selectedProduct.mainProductCode,
                ),
                _IdentityRow(
                  label: 'Product name',
                  value: selectedProduct.productName,
                ),
                _IdentityRow(
                  label: 'Product-level barcode',
                  value: selectedProduct.productBarcode ?? 'Not provided',
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),
        for (final variant in selectedProduct.variants) ...[
          VariantDetailsCard(variant: variant),
          const SizedBox(height: 12),
        ],
      ],
    );
  }
}

final class VariantDetailsCard extends StatelessWidget {
  const VariantDetailsCard({required this.variant, super.key});

  final CatalogueVariantPresentation variant;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Sellable variant',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          _IdentityRow(
            label: 'Variant ID',
            value: '${variant.productVariantId}',
          ),
          _IdentityRow(label: 'SKU', value: variant.sku ?? 'Not provided'),
          _IdentityRow(
            label: 'Variant barcode',
            value: variant.barcode ?? 'Not provided',
          ),
          for (final attribute in variant.attributes)
            _IdentityRow(label: attribute.name, value: attribute.value),
        ],
      ),
    ),
  );
}

final class _IdentityRow extends StatelessWidget {
  const _IdentityRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 8),
    child: Wrap(
      spacing: 8,
      runSpacing: 4,
      children: [
        Text('$label:', style: Theme.of(context).textTheme.labelLarge),
        SelectableText(value),
      ],
    ),
  );
}

final class BarcodeResolutionPanel extends StatelessWidget {
  const BarcodeResolutionPanel({
    required this.barcode,
    required this.state,
    super.key,
  });

  final String? barcode;
  final BarcodeResolutionState state;

  @override
  Widget build(BuildContext context) {
    final (title, message) = switch (state) {
      BarcodeResolutionState.unknown => (
        'Barcode not recognized',
        'No exact authorized variant match is available. Verify the barcode or ask an administrator; no product was selected.',
      ),
      BarcodeResolutionState.ambiguous => (
        'Barcode match is ambiguous',
        'More than one eligible match may exist. Do not select a variant until the ERP resolves the ambiguity.',
      ),
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (barcode != null) ...[
          Text(
            'Scanned barcode',
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 4),
          SelectableText(
            barcode!,
            key: const ValueKey('raw-barcode'),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
        ],
        ContentStatePanel(
          state: ContentState.error,
          title: title,
          message: message,
        ),
      ],
    );
  }
}
