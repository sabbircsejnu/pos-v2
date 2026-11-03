import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:iranimart_business_mobile/features/catalogue/domain/catalogue_presentation_models.dart';
import 'package:iranimart_business_mobile/features/catalogue/presentation/catalogue_page.dart';

void main() {
  testWidgets('catalogue search fields and filters remain presentation-only', (
    tester,
  ) async {
    await _pumpCatalogue(tester);

    expect(find.text('Business 42'), findsOneWidget);
    expect(find.text('North Outlet'), findsOneWidget);
    expect(find.textContaining('not an authorization grant'), findsOneWidget);
    expect(find.text('Catalogue not connected'), findsOneWidget);
    expect(
      find.byKey(const ValueKey('catalogue-search-query')),
      findsOneWidget,
    );

    await tester.tap(
      find.byType(DropdownButtonFormField<CatalogueSearchField>),
    );
    await tester.pumpAndSettle();
    for (final label in ['Product name', 'SKU', 'Barcode']) {
      expect(find.text(label), findsOneWidget);
    }

    await tester.tap(find.text('SKU').last);
    await tester.pumpAndSettle();
    for (final filter in [
      'category',
      'size',
      'colour',
      'attribute-name',
      'attribute-value',
    ]) {
      final filterFinder = find.byKey(ValueKey('catalogue-filter-$filter'));
      await tester.scrollUntilVisible(
        filterFinder,
        220,
        scrollable: find.byType(Scrollable).first,
      );
      expect(filterFinder, findsOneWidget);
    }
  });

  testWidgets('product and variant identity preserve large IDs and raw codes', (
    tester,
  ) async {
    const product = CatalogueProductPresentation(
      productId: 4294967297,
      mainProductCode: 'ABA100P',
      productName: 'Presentation fixture',
      productBarcode: '00012345',
      variants: [
        CatalogueVariantPresentation(
          productVariantId: 9007199254740991,
          sku: 'SKU-XL-BLK',
          barcode: '0000123456789',
          attributes: [
            CatalogueAttributePresentation(name: 'Colour', value: 'Black'),
            CatalogueAttributePresentation(name: 'Size', value: 'XL'),
          ],
        ),
      ],
    );

    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: ProductDetailsPanel(product: product)),
      ),
    );

    expect(find.text('4294967297'), findsOneWidget);
    expect(find.text('9007199254740991'), findsOneWidget);
    expect(find.text('ABA100P'), findsOneWidget);
    expect(find.text('00012345'), findsOneWidget);
    expect(find.text('0000123456789'), findsOneWidget);
    expect(find.text('Product-level barcode:'), findsOneWidget);
    expect(find.text('Variant barcode:'), findsOneWidget);
    expect(find.text('Black'), findsOneWidget);
    expect(find.text('XL'), findsOneWidget);
    expect(find.textContaining('Price'), findsNothing);
    expect(find.textContaining('Stock'), findsNothing);
  });

  testWidgets(
    'empty, unavailable, stale, loading, error and unauthorized states are explicit',
    (tester) async {
      const expectedTitles = {
        CatalogueContentState.loading: 'Loading catalogue',
        CatalogueContentState.empty: 'No products found',
        CatalogueContentState.unavailable: 'Catalogue unavailable',
        CatalogueContentState.stale: 'Catalogue may be out of date',
        CatalogueContentState.error: 'Catalogue could not be loaded',
        CatalogueContentState.unauthorized: 'Catalogue access not authorized',
      };

      for (final entry in expectedTitles.entries) {
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(body: CatalogueStatusPanel(state: entry.key)),
          ),
        );
        expect(find.text(entry.value), findsOneWidget);
        expect(tester.takeException(), isNull);
      }
    },
  );

  testWidgets(
    'unknown and ambiguous barcode states preserve the scanned string',
    (tester) async {
      for (final (state, title) in [
        (BarcodeResolutionState.unknown, 'Barcode not recognized'),
        (BarcodeResolutionState.ambiguous, 'Barcode match is ambiguous'),
      ]) {
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: BarcodeResolutionPanel(
                barcode: '0000123456789',
                state: state,
              ),
            ),
          ),
        );

        expect(find.text(title), findsOneWidget);
        expect(find.text('0000123456789'), findsOneWidget);
        expect(
          find.textContaining(
            state == BarcodeResolutionState.unknown
                ? 'no product was selected'
                : 'Do not select a variant',
          ),
          findsOneWidget,
        );
        expect(tester.takeException(), isNull);
      }
    },
  );

  testWidgets('catalogue scrolls at compact width with large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: const MediaQueryData(
            size: Size(320, 640),
            textScaler: TextScaler.linear(2),
          ),
          child: const Scaffold(
            body: CataloguePage(businessId: 42, outletName: 'North Outlet'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Product catalogue'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.text('Product details'),
      240,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Product details'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}

Future<void> _pumpCatalogue(WidgetTester tester) async {
  await tester.pumpWidget(
    const MaterialApp(
      home: Scaffold(
        body: CataloguePage(businessId: 42, outletName: 'North Outlet'),
      ),
    ),
  );
  await tester.pumpAndSettle();
}
