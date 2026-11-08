import { createSale, onHand } from '../support/api';
import { barcode, ids, outletA, outletB, price, terminalA, terminalB, uatPassword, users } from '../support/env';
import {
  addReplacement, approve, as, confirmExchange, expect, findInvoice, next, openExchange, payCash, prepareExchange, refundBy,
  returnItem, snap, test, toSettle, totalsValue
} from '../support/ui';

// Default actor for this file: the outlet A salesperson (template SalesPerson role).
test.use(as('salesA'));

test.describe('Lookup and validation (U-03, product and quantity checks)', () => {
  test.use(as('salesA'));

  test('U-03 an unknown invoice number is reported and nothing is loaded', async ({ page }) => {
    await openExchange(page);
    await findInvoice(page, 'NO-SUCH-INVOICE-UAT');
    await expect(page.getByText('No invoice with that number was found')).toBeVisible();
    await expect(page.locator('ul[aria-label="Selected invoices"]')).toHaveCount(0);
    await snap(page, 'U-03-unknown-invoice');
  });

  test('a scanned item that is not on the invoice, an unknown replacement and an over-return are refused', async ({ page }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    await openExchange(page);
    await findInvoice(page, sale.number);
    await next(page);
    await page.locator('input[name="returnScan"]').fill(barcode('WATCH'));
    await page.getByRole('button', { name: 'Match', exact: true }).click();
    await expect(page.locator('app-ui-form-field').filter({ hasText: 'Scan returned item' })).toContainText(/not|no /i);
    await snap(page, 'V-01-returned-item-not-on-invoice');

    await returnItem(page, 'UAT Shirt', 3);
    // Next stays disabled and says why while a returned quantity exceeds what remains.
    await expect(page.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
    await expect(page.getByTestId('step-pending')).toBeVisible();
    await snap(page, 'V-02-over-return');
    await page.locator('ul[aria-label="Returned items"] li input[name="returnQty"]').fill('1');
    await next(page);
    await page.getByLabel('Find replacement product').fill('UAT-NOT-A-PRODUCT');
    await page.getByLabel('Find replacement product').press('Enter');
    await expect(page.getByText('No matching product')).toBeVisible();
    await snap(page, 'V-03-unknown-replacement');
  });
});

test.describe('Core exchanges (U-11, U-15, multi-invoice, walk-in rule)', () => {
  test.use(as('salesA'));

  test('U-11 two returned lines for two replacements: server totals and stock', async ({ page }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [
      { code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }, { code: 'CAP', quantity: 2, unitPrice: price.CAP }]);
    const before = { cap: onHand(outletA(), 'CAP'), coat: onHand(outletA(), 'COAT') };
    await prepareExchange(page, {
      invoices: [sale.number],
      returns: [{ product: 'UAT Shirt', quantity: 1 }, { product: 'UAT Cap', quantity: 2 }],
      replacements: [{ barcode: barcode('JACKET') }, { barcode: barcode('COAT') }]
    });
    expect(await totalsValue(page, 'Returned credit')).toBe(220);   // 110 + 2 × 55
    expect(await totalsValue(page, 'Replacements')).toBe(462);      // 132 + 330
    expect(await totalsValue(page, 'Difference')).toBe(242);
    await snap(page, 'U-11-review');
    await toSettle(page);
    await payCash(page, 242, 250);
    const number = await confirmExchange(page);
    await snap(page, 'U-11-completed');
    expect(onHand(outletA(), 'CAP')).toBe(before.cap + 2);
    expect(onHand(outletA(), 'COAT')).toBe(before.coat - 1);
    expect(number).toMatch(/^UATAX/);
  });

  test('U-15 an even exchange needs no settlement and still posts', async ({ page }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('SHIRT') }] });
    expect(await totalsValue(page, 'Difference')).toBe(0);
    await toSettle(page);
    await confirmExchange(page);
    await snap(page, 'U-15-even-completed');
  });

  test('two invoices of the same registered customer are combined in one exchange', async ({ page }) => {
    const first = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }], { customerId: ids.customerId });
    const second = await createSale('salesA', outletA(), terminalA(), [{ code: 'CAP', quantity: 1, unitPrice: price.CAP }], { customerId: ids.customerId });
    await prepareExchange(page, {
      invoices: [first.number, second.number],
      returns: [{ product: 'UAT Shirt', quantity: 1 }, { product: 'UAT Cap', quantity: 1 }],
      replacements: [{ barcode: barcode('COAT') }]
    });
    expect(await totalsValue(page, 'Returned credit')).toBe(165);
    expect(await totalsValue(page, 'Difference')).toBe(165);
    await snap(page, 'U-11b-two-invoices-review');
    await toSettle(page);
    await payCash(page, 165, 200);
    await confirmExchange(page);
    await snap(page, 'U-11b-two-invoices-completed');
  });

  test('walk-in invoices cannot be combined (D-03)', async ({ page }) => {
    const first = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    const second = await createSale('salesA', outletA(), terminalA(), [{ code: 'CAP', quantity: 1, unitPrice: price.CAP }]);
    await openExchange(page);
    await findInvoice(page, first.number);
    await expect(page.locator('ul[aria-label="Selected invoices"]')).toContainText(first.number);
    await findInvoice(page, second.number);
    await expect(page.getByText(/walk-in/i).first()).toBeVisible();
    await expect(page.locator('ul[aria-label="Selected invoices"]')).not.toContainText(second.number);
    await snap(page, 'D-03-walk-in-combination-refused');
  });
});

test.describe('Refunds (U-12, U-13, U-14)', () => {
  test('U-12 an outlet manager with refund permission refunds a lower-value exchange in cash', async ({ browser }) => {
    const context = await browser.newContext(as('managerA'));
    const page = await context.newPage();
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'JACKET', quantity: 1, unitPrice: price.JACKET }]);
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Jacket', quantity: 1 }], replacements: [{ barcode: barcode('CAP') }] });
    expect(await totalsValue(page, 'Difference')).toBe(-77);
    await toSettle(page);
    await refundBy(page, 'Cash');
    await snap(page, 'U-12-refund-settle');
    await confirmExchange(page);
    await snap(page, 'U-12-refund-completed');
    await context.close();
  });

  test('U-14 a card refund needs a reference and confirmation before Confirm is enabled', async ({ browser }) => {
    const context = await browser.newContext(as('managerA'));
    const page = await context.newPage();
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'JACKET', quantity: 1, unitPrice: price.JACKET }]);
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Jacket', quantity: 1 }], replacements: [{ barcode: barcode('CAP') }] });
    await toSettle(page);
    await refundBy(page, 'Card');
    await expect(page.getByTestId('confirm-exchange')).toBeDisabled();
    await snap(page, 'U-14-card-refund-needs-reference');
    await page.locator('input[name="refundReference"]').fill('UAT-CARD-REF-001');
    await page.locator('input[name="refundConfirmed"]').check();
    await expect(page.getByTestId('confirm-exchange')).toBeEnabled();
    await confirmExchange(page);
    await snap(page, 'U-14-card-refund-completed');
    await context.close();
  });

  test('U-13 a salesperson without refund permission needs a supervisor refund approval (OC-03 Case B)', async ({ page }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'JACKET', quantity: 1, unitPrice: price.JACKET }]);
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Jacket', quantity: 1 }], replacements: [{ barcode: barcode('CAP') }] });
    await toSettle(page);
    await refundBy(page, 'Cash');
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText(/refund/i);
    await expect(page.getByTestId('confirm-exchange')).toBeDisabled();
    await approve(page, users.managerA, uatPassword(), 'Customer prefers a cheaper item');
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText('Approved');
    await snap(page, 'U-13-refund-approved');
    await confirmExchange(page);
    await snap(page, 'U-13-completed');
  });
});

test.describe('Cross-outlet (U-30)', () => {
  test.use(as('salesA'));

  test('an invoice sold at outlet B is exchanged at outlet A; stock is received at A', async ({ page }) => {
    const sale = await createSale('salesB', outletB(), terminalB(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    const before = { shirtA: onHand(outletA(), 'SHIRT'), shirtB: onHand(outletB(), 'SHIRT') };
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('JACKET') }] });
    await expect(page.locator('table').first()).toBeVisible();
    await toSettle(page);
    await payCash(page, 22, 22);
    await confirmExchange(page);
    await snap(page, 'U-30-cross-outlet-completed');
    expect(onHand(outletA(), 'SHIRT')).toBe(before.shirtA + 1);
    expect(onHand(outletB(), 'SHIRT')).toBe(before.shirtB);
  });
});
