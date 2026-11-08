import type { Page } from '@playwright/test';
import { call, createSale, token, unique } from '../support/api';
import { ids, outletA, outletB, price, terminalA, uatPassword, users } from '../support/env';
import { approve, as, chooseReason, confirmExchange, expect, findInvoice, next, openExchange, payCash, returnItem, review, snap, test, toSettle } from '../support/ui';

test.use(as('salesA'));

const borka = (sku: string) => ids.variants[sku];
const results = (page: Page) => page.getByRole('listbox', { name: 'Matching products' }).getByRole('option');

/** Synthetic stock for the multi-variant product: three variants at outlet A, Navy/L only at outlet B (out of stock at A). */
test.beforeAll(async () => {
  const owner = token('owner');
  for (const [locationId, skus] of [[outletA(), ['ABA852P-BLA-M', 'ABA852P-BLA-L', 'ABA852P-NAV-M']], [outletB(), ['ABA852P-NAV-L']]] as const) {
    await call(owner, 'POST', '/purchase-orders/purchase-receive', {
      supplierId: ids.supplierId, locationType: 'outlet', locationId, orderDate: new Date().toISOString(), idempotencyKey: unique('grn-borka'),
      items: skus.map(sku => ({ variantId: borka(sku), quantity: 5, unitPrice: 900, discount: 0, tax: 0 }))
    });
  }
});

async function toReplacements(page: Page): Promise<void> {
  const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
  await openExchange(page);
  await findInvoice(page, sale.number);
  await expect(page.locator('ul[aria-label="Selected invoices"]')).toContainText(sale.number);
  await next(page);
  await returnItem(page, 'UAT Shirt', 1);
  await next(page);
  await expect(page.getByLabel('Find replacement product')).toBeVisible();
}

async function search(page: Page, text: string): Promise<void> {
  const box = page.getByLabel('Find replacement product');
  await box.fill(text);
  await expect(page.getByRole('listbox', { name: 'Matching products' }).or(page.getByText('No matching product'))).toBeVisible();
}

test.describe('Step 1 invoice lookup layout', () => {
  for (const width of [1440, 768, 390]) {
    test(`input and Find invoice are aligned on one row at ${width}px; guidance and errors sit below`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await openExchange(page);
      const input = page.locator('#exchange-invoice-number');
      const button = page.getByRole('button', { name: /Find invoice/ });
      await expect(button).toBeDisabled();
      await expect(page.getByTestId('step-pending')).toHaveText('Add the original invoice to continue.');
      await expect(page.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
      await snap(page, `S1-after-${width}-empty`);

      await input.fill('NO-SUCH-INVOICE-UAT');
      await button.click();
      await expect(page.locator('#exchange-invoice-error')).toContainText('No invoice with that number was found');
      const [inputBox, buttonBox, errorBox] = await Promise.all([input.boundingBox(), button.boundingBox(), page.locator('#exchange-invoice-error').boundingBox()]);
      expect(Math.abs(inputBox!.y - buttonBox!.y)).toBeLessThanOrEqual(1);
      expect(Math.abs(inputBox!.height - buttonBox!.height)).toBeLessThanOrEqual(1);
      expect(errorBox!.y).toBeGreaterThanOrEqual(inputBox!.y + inputBox!.height);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      await snap(page, `S1-after-${width}-error`);
    });
  }
});

test.describe('Replacement product search', () => {
  test('main product code ABA852P lists every variant with code, attributes, SKU, barcode, price and outlet stock', async ({ page }) => {
    await toReplacements(page);
    await search(page, 'ABA852P');
    const options = results(page);
    await expect(options).toHaveCount(4);
    const list = page.getByRole('listbox', { name: 'Matching products' });
    await expect(list).toContainText('Ari Wasenat Premium Borka');
    // Attribute order follows the stored JSON; check each attribute of each variant.
    for (const sku of ['ABA852P-BLA-M', 'ABA852P-NAV-L']) {
      const row = page.getByTestId(`replacement-result-${borka(sku)}`);
      await expect(row).toContainText(`Colour: ${sku.includes('BLA') ? 'Black' : 'Navy'}`);
      await expect(row).toContainText(`Size: ${sku.endsWith('M') ? 'M' : 'L'}`);
    }
    await expect(list).toContainText('ABA852P-NAV-M');
    await expect(list).toContainText('UATABA852PBLAM');
    await expect(list).toContainText('2,450.00');
    await expect(list).toContainText('2,550.00'); // Navy/L carries a price adjustment
    await expect(page.getByTestId(`replacement-result-${borka('ABA852P-NAV-L')}`)).toContainText('Out of stock');
    await expect(page.getByTestId(`replacement-result-${borka('ABA852P-BLA-M')}`)).toContainText(/In stock \d+/);
    await expect(list).not.toContainText(/cost/i);
    await snap(page, 'S3-search-ABA852P-results');

    // Keyboard: ↓ moves, Enter adds the highlighted variant.
    const box = page.getByLabel('Find replacement product');
    await box.press('ArrowDown');
    const active = await box.getAttribute('aria-activedescendant');
    const activeText = await page.locator(`#${active}`).innerText();
    await box.press('Enter');
    const line = page.locator('ul[aria-label="Replacement items"] li').first();
    await expect(line).toContainText('Ari Wasenat Premium Borka');
    await expect(line).toContainText('Code ABA852P');
    await expect(line).toContainText(activeText.includes('Black') ? 'Black' : 'Navy');
    await expect(line).toContainText(/Stock here \d+/);
    await expect(line).toContainText(/Price/);
    await expect(box).toBeFocused();
    await snap(page, 'S3-selected-replacement-details');
  });

  test('an out-of-stock variant cannot be added; a scanned barcode adds its exact variant at once; repeat adds one more', async ({ page }) => {
    await toReplacements(page);
    await search(page, 'ABA852P');
    // The option is aria-disabled (out of stock); a forced click checks the explanation shown.
    await page.getByTestId(`replacement-result-${borka('ABA852P-NAV-L')}`).click({ force: true });
    await expect(page.getByText('is out of stock at this outlet')).toBeVisible();
    await expect(page.locator('ul[aria-label="Replacement items"]')).toHaveCount(0);

    const box = page.getByLabel('Find replacement product');
    await box.fill('UATABA852PBLAL');
    await box.press('Enter');
    const line = page.getByTestId(`replacement-line-${borka('ABA852P-BLA-L')}`);
    await expect(line).toContainText('Colour: Black');
    await expect(line).toContainText('Size: L');
    await box.fill('UATABA852PBLAL');
    await box.press('Enter');
    await expect(line.locator('input[name="replacementQty"]')).toHaveValue('2');
    await expect(page.locator('ul[aria-label="Replacement items"] li')).toHaveCount(1);
    await snap(page, 'S3-barcode-scan-and-repeat');
  });

  test('partial name and partial SKU find the product; an unknown term says so', async ({ page }) => {
    await toReplacements(page);
    await search(page, 'wasenat');
    await expect(results(page)).toHaveCount(4);
    await search(page, 'NAV-M');
    await expect(results(page)).toHaveCount(1);
    await expect(results(page).first()).toContainText('Colour: Navy');
    await expect(results(page).first()).toContainText('Size: M');
    await search(page, 'NO-SUCH-PRODUCT-UAT');
    await expect(page.getByText('No matching product')).toBeVisible();
    await snap(page, 'S3-no-results');
  });

  test('responsive: search results and the selected list fit at 390 and 768 px', async ({ page }) => {
    for (const width of [390, 768]) {
      await page.setViewportSize({ width, height: 900 });
      await toReplacements(page);
      await search(page, 'ABA852P');
      await snap(page, `S3-search-${width}`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      await page.getByTestId(`replacement-result-${borka('ABA852P-NAV-M')}`).click();
      await expect(page.locator('ul[aria-label="Replacement items"] li')).toHaveCount(1);
      await snap(page, `S3-selected-${width}`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
    }
  });

  test('a replacement chosen by product code completes an exchange (server price, high-value approval, cash)', async ({ page }) => {
    await toReplacements(page);
    await search(page, 'ABA852P');
    await page.getByTestId(`replacement-result-${borka('ABA852P-BLA-M')}`).click();
    await next(page);
    await chooseReason(page);
    await review(page);
    await expect(page.getByTestId('preview-totals')).toContainText('2,695.00'); // 2,450 + 10% VAT, from the server
    await toSettle(page);
    await approve(page, users.managerA, uatPassword(), 'High-value borka replacement checked');
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText('Approved');
    await payCash(page, 2585, 2600);
    const number = await confirmExchange(page);
    await expect(page.locator('#receipt-print')).toContainText('Ari Wasenat Premium Borka');
    await snap(page, 'S3-exchange-completed-with-searched-product');
    expect(number).toMatch(/^UATAX/);
  });
});
