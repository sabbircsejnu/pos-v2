import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Page } from '@playwright/test';
import { call, createSale, onHand, token } from '../support/api';
import { barcode, EVIDENCE, outletA, price, terminalA, uatPassword, users } from '../support/env';
import {
  approvalSlot, approve, as, confirmExchange, expect, findInvoice, next, openExchange, payCash, prepareExchange, refundBy,
  returnItem, review, snap, test, toSettle, totalsValue, chooseReason, addReplacement
} from '../support/ui';

test.use(as('salesA'));

function daysAgoLocal(days: number): string {
  const date = new Date(Date.now() - days * 86_400_000);
  return `${date.toISOString().slice(0, 10)}T10:00:00`;
}

async function exchangeShirtForShirt(page: Page, invoice: string, product = 'UAT Shirt'): Promise<string> {
  await prepareExchange(page, { invoices: [invoice], returns: [{ product, quantity: 1 }], replacements: [{ barcode: barcode('SHIRT') }] });
  await toSettle(page);
  return confirmExchange(page);
}

async function searchHistory(page: Page, number: string): Promise<void> {
  await page.goto('/exchanges');
  await page.getByPlaceholder('Exchange no, invoice no or customer…').fill(number);
  await expect(page.getByRole('row').filter({ hasText: number })).toHaveCount(1);
}

async function openDetailOf(page: Page, number: string): Promise<void> {
  await searchHistory(page, number);
  await page.getByRole('row').filter({ hasText: number }).click();
  await expect(page.getByText('Original invoices')).toBeVisible();
}

test.describe('Supervisor approvals (U-20, U-21, U-25, U-26)', () => {
  test('U-20/U-21 an expired window needs a supervisor of the receiving outlet', async ({ page }) => {
    const sale = await createSale('owner', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }], { salesDate: daysAgoLocal(9) });
    await openExchange(page);
    await findInvoice(page, sale.number);
    await expect(page.getByText(/Window expired/)).toBeVisible();
    await snap(page, 'U-20-window-expired-badge');
    await next(page);
    await returnItem(page, 'UAT Shirt', 1);
    await next(page);
    await addReplacement(page, barcode('SHIRT'));
    await next(page);
    await chooseReason(page);
    await review(page);
    await toSettle(page);
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText(/window/i);
    await expect(page.getByTestId('confirm-exchange')).toBeDisabled();

    // Supervisor B has the permission but no access to outlet A (OC-01).
    await approve(page, users.managerB, uatPassword(), 'Customer was travelling');
    await expect(page.getByTestId('approval-error')).toContainText(/permission or access to this outlet/);
    await snap(page, 'U-21-supervisor-without-outlet-access');
    await page.getByRole('dialog', { name: 'Supervisor approval' }).getByRole('button', { name: 'Cancel' }).click();

    await approve(page, users.managerA, uatPassword(), 'Customer was travelling');
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText('Approved');
    const number = await confirmExchange(page);
    await snap(page, 'U-20-window-override-completed');
    expect(number).toMatch(/^UATAX/);
  });

  test('U-25 a replacement worth at least the threshold needs a high-value approval (OC-05, C-10)', async ({ page }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('WATCH') }] });
    expect(await totalsValue(page, 'Replacements')).toBe(660);
    await expect(page.getByText(/high.value/i).first()).toBeVisible();
    await toSettle(page);
    await approve(page, users.managerA, uatPassword(), 'High-value replacement checked');
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText('Approved');
    await payCash(page, 550, 550);
    await confirmExchange(page);
    await snap(page, 'U-25-high-value-completed');
  });

  test('U-26 re-exchange by exchange number; the third exchange in the lineage needs a chain approval (OC-02)', async ({ page }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    const first = await exchangeShirtForShirt(page, sale.number);
    await page.getByRole('button', { name: 'New exchange' }).click();
    const second = await exchangeShirtForShirt(page, first);
    await snap(page, 'U-26-second-exchange-from-exchange-number');
    await page.getByRole('button', { name: 'New exchange' }).click();
    await prepareExchange(page, { invoices: [second], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('SHIRT') }] });
    await expect(page.getByText(/chain/i).first()).toBeVisible();
    await toSettle(page);
    await approve(page, users.managerA, uatPassword(), 'Customer still unhappy with the size');
    await expect(page.locator('ul[aria-label="Required approvals"]')).toContainText('Approved');
    await confirmExchange(page);
    await snap(page, 'U-26-chain-override-completed');

    // U-51: the first exchange's replacement was exchanged again, so it cannot be reversed (C-05).
    const manager = await page.context().browser()!.newContext(as('managerA'));
    const managerPage = await manager.newPage();
    await openDetailOf(managerPage, first);
    await managerPage.getByRole('button', { name: /Reverse exchange/ }).first().click();
    await expect(managerPage.getByTestId('reversal-blocked')).toBeVisible();
    await snap(managerPage, 'U-51-reversal-blocked-by-re-exchange');
    await manager.close();
  });
});

test.describe('History, receipts and reversal (U-40…U-43, U-50, U-52, U-05, U-60, U-61)', () => {
  test('complete, find, print, download, reverse and reconcile one exchange end to end', async ({ page, browser }) => {
    const date = new Date().toISOString().slice(0, 10);
    const ownerToken = token('owner');
    const dashboard = async () => (await call<any>(ownerToken, 'GET',
      `/v1/owner-dashboard/sales/summary?from=${date}&to=${date}&outletId=${outletA()}`)).netSales?.value ?? 0;

    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    const netBefore = await dashboard();
    const stockBefore = { shirt: onHand(outletA(), 'SHIRT'), jacket: onHand(outletA(), 'JACKET') };

    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('JACKET') }] });
    await toSettle(page);
    await payCash(page, 22, 22);
    const number = await confirmExchange(page);

    // U-42 print (the browser print dialog is stubbed so the run stays headless) and U-43 PDF download.
    await page.evaluate(() => { (window as any).__printed = 0; window.print = () => { (window as any).__printed++; }; });
    await page.getByRole('button', { name: /Print 80mm/ }).click();
    await expect.poll(() => page.evaluate(() => (window as any).__printed)).toBe(1);
    mkdirSync(resolve(EVIDENCE, 'downloads'), { recursive: true });
    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: /Download PDF/ }).click()]);
    const pdfPath = resolve(EVIDENCE, 'downloads', `${number}.pdf`);
    await download.saveAs(pdfPath);
    expect(download.suggestedFilename()).toMatch(/\.pdf$/);

    // U-60 dashboard: +20 net of VAT on the exchange business date at the receiving outlet.
    expect(await dashboard()).toBeCloseTo(netBefore + 20, 2);

    // U-40/U-41 history search and detail.
    await searchHistory(page, number);
    await snap(page, 'U-40-history-search');
    await page.getByRole('row').filter({ hasText: number }).click();
    await expect(page.getByText('Original invoices')).toBeVisible();
    await expect(page.getByText(sale.number).first()).toBeVisible();
    await snap(page, 'U-41-detail');

    // U-52 the salesperson cannot reverse.
    await expect(page.getByRole('button', { name: /Reverse exchange/ })).toHaveCount(0);

    // U-50 the outlet manager reverses with a cash refund of the collected 22.
    const manager = await browser.newContext(as('managerA'));
    const managerPage = await manager.newPage();
    await openDetailOf(managerPage, number);
    await managerPage.getByRole('button', { name: /Reverse exchange/ }).first().click();
    const dialog = managerPage.getByRole('dialog', { name: 'Reverse exchange' });
    await dialog.locator('textarea[name="reverseReason"]').fill('Customer returned the jacket the same day');
    await dialog.locator('fieldset').filter({ hasText: 'Refund method' }).getByLabel('Cash').check();
    await snap(managerPage, 'U-50-reverse-dialog');
    await dialog.getByRole('button', { name: 'Reverse exchange' }).click();
    await expect(dialog).toBeHidden({ timeout: 30_000 });
    await expect(managerPage.locator('app-ui-card').filter({ hasText: 'Summary' }).first()).toContainText(/reversed/i);
    await snap(managerPage, 'U-50-reversed-detail');
    await manager.close();

    expect(onHand(outletA(), 'SHIRT')).toBe(stockBefore.shirt);
    expect(onHand(outletA(), 'JACKET')).toBe(stockBefore.jacket);
    // U-61 same business date: the reversal offsets the exchange.
    expect(await dashboard()).toBeCloseTo(netBefore, 2);

    // U-05 the reversed exchange number is not a re-exchange source and says why (I-12 item 3).
    await openExchange(page);
    await findInvoice(page, number);
    await expect(page.getByText('That exchange was reversed')).toBeVisible();
    await snap(page, 'U-05-reversed-number-lookup');

    // Owner dashboard page renders for the day (evidence).
    const owner = await browser.newContext(as('owner'));
    const ownerPage = await owner.newPage();
    await ownerPage.goto('/dashboard/sales-finance');
    await ownerPage.waitForLoadState('networkidle');
    await snap(ownerPage, 'U-60-owner-dashboard-sales-finance');
    await owner.close();
  });
});
