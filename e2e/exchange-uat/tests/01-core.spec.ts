import { createSale, onHand } from '../support/api';
import { barcode, outletA, price, terminalA, uatPassword, users } from '../support/env';
import { addReplacement, as, chooseReason, confirmExchange, expect, findInvoice, next, openExchange, payCash, returnItem, review, snap, test, totalsValue } from '../support/ui';

test.describe('U-01 sign-in and entry', () => {
  test('a salesperson signs in through the login form and reaches the exchange screen from POS', async ({ page, problems }) => {
    await page.goto('/login');
    await page.locator('input[type="email"]').fill(users.salesA);
    await page.locator('input[type="password"]').fill(uatPassword());
    await page.getByRole('button', { name: 'Sign In' }).click();
    await page.waitForURL(url => !url.pathname.startsWith('/login'));
    await openExchange(page);
    await snap(page, 'U-01-exchange-screen-sales-a');
    expect(problems.consoleErrors).toEqual([]);
  });
});

test.describe('U-10 higher-value exchange with cash collection', () => {
  test.use(as('salesA'));

  test('return 1 Shirt for 1 Jacket: collect 22, stock moves both ways', async ({ page, problems }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 2, unitPrice: price.SHIRT }]);
    const shirtBefore = onHand(outletA(), 'SHIRT');
    const jacketBefore = onHand(outletA(), 'JACKET');

    await openExchange(page);
    await findInvoice(page, sale.number);
    await expect(page.locator('ul[aria-label="Selected invoices"]')).toContainText(sale.number);
    await next(page);
    await returnItem(page, 'UAT Shirt', 1);
    await next(page);
    await addReplacement(page, barcode('JACKET'));
    await next(page);
    await chooseReason(page);
    await review(page);
    expect(await totalsValue(page, 'Returned credit')).toBe(110);
    expect(await totalsValue(page, 'Replacements')).toBe(132);
    expect(await totalsValue(page, 'Difference')).toBe(22);
    await snap(page, 'U-10-review');
    await next(page);
    await payCash(page, 22, 50);
    await snap(page, 'U-10-settle');
    const number = await confirmExchange(page);
    expect(number).toMatch(/^UATAX\d{6}/);
    await expect(page.locator('#receipt-print')).toContainText(number);
    await snap(page, 'U-10-completed-receipt');

    expect(onHand(outletA(), 'SHIRT')).toBe(shirtBefore + 1);
    expect(onHand(outletA(), 'JACKET')).toBe(jacketBefore - 1);
    expect(problems.consoleErrors).toEqual([]);
  });
});
