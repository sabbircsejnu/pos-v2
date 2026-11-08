import { test as base, expect, type Page } from '@playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { statePath } from './api';
import { EVIDENCE, type UatUser } from './env';

export interface Problems { consoleErrors: string[]; failedRequests: string[] }

/** Every test records browser console errors and failed (>=400 or network-failed) requests as evidence. */
export const test = base.extend<{ problems: Problems }>({
  problems: [async ({ page }, use, testInfo) => {
    const problems: Problems = { consoleErrors: [], failedRequests: [] };
    page.on('console', message => { if (message.type() === 'error') problems.consoleErrors.push(message.text()); });
    page.on('pageerror', error => problems.consoleErrors.push(`pageerror: ${error.message}`));
    page.on('requestfailed', request => problems.failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`));
    page.on('response', response => {
      if (response.status() >= 400 && response.url().includes('/api/'))
        problems.failedRequests.push(`${response.request().method()} ${response.url()} -> ${response.status()}`);
    });
    await use(problems);
    const dir = resolve(EVIDENCE, 'network-console');
    mkdirSync(dir, { recursive: true });
    const name = testInfo.titlePath.slice(1).join(' - ').replace(/[^\w.-]+/g, '_').slice(0, 150);
    writeFileSync(resolve(dir, `${name}.json`), JSON.stringify(problems, null, 2));
    await testInfo.attach('console-and-network', { body: JSON.stringify(problems, null, 2), contentType: 'application/json' });
  }, { auto: true }]
});
export { expect };

export const as = (user: UatUser) => ({ storageState: statePath(user) });

export async function snap(page: Page, name: string): Promise<void> {
  mkdirSync(resolve(EVIDENCE, 'screenshots'), { recursive: true });
  await page.screenshot({ path: resolve(EVIDENCE, 'screenshots', `${name}.png`), fullPage: true });
}

// ---- Exchange workflow driver (drives the real UI only) ----

export async function openExchange(page: Page): Promise<void> {
  await page.goto('/pos/exchange');
  await expect(page.getByRole('heading', { name: 'Product Exchange' })).toBeVisible();
  await expect(page.locator('.ex-context')).toContainText('Open ·');
}

export async function findInvoice(page: Page, number: string): Promise<void> {
  const input = page.locator('input[name="invoiceNumber"]');
  await input.fill(number);
  await page.getByRole('button', { name: /^(Find|Add) invoice$/ }).click();
}

export async function next(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Next', exact: true }).click();
}

export async function returnItem(page: Page, product: string, quantity?: number, sellable = true): Promise<void> {
  await page.getByRole('button', { name: new RegExp(`^Return ${product}`) }).first().click();
  const row = page.locator('ul[aria-label="Returned items"] li').filter({ hasText: product }).last();
  if (quantity !== undefined) await row.locator('input[name="returnQty"]').fill(String(quantity));
  // The cashier confirms the condition explicitly; only sellable items can be exchanged.
  if (sellable) await row.locator('input[name="conditionSellable"]').check();
}

/** Adds a replacement through the product search: an exact barcode/SKU + Enter adds it at once (scanner path). */
export async function addReplacement(page: Page, code: string, quantity?: number): Promise<void> {
  const before = await page.locator('ul[aria-label="Replacement items"] li').count();
  const box = page.getByLabel('Find replacement product');
  await box.fill(code);
  await box.press('Enter');
  await expect(page.locator('ul[aria-label="Replacement items"] li')).toHaveCount(before + 1);
  if (quantity !== undefined)
    await page.locator('ul[aria-label="Replacement items"] li').last().locator('input[name="replacementQty"]').fill(String(quantity));
}

export async function chooseReason(page: Page, code = 'size_fitting'): Promise<void> {
  await page.locator('select[formcontrolname="reasonCode"]').selectOption(code);
}

/** Moves from the current step to Review and waits for the server totals. */
export async function review(page: Page): Promise<void> {
  const totals = page.getByTestId('preview-totals');
  for (let i = 0; i < 6 && !(await totals.isVisible()); i++) {
    const reviewButton = page.getByRole('button', { name: 'Review exchange' });
    if (await reviewButton.isVisible()) await reviewButton.click();
    else if (await page.getByRole('button', { name: 'Next', exact: true }).isVisible()) await next(page);
    await page.waitForTimeout(300);
  }
  await expect(totals).toBeVisible();
}

export async function payCash(page: Page, amount: number, tendered = amount): Promise<void> {
  await page.getByLabel('Cash amount').fill(String(amount));
  await page.locator('input[name="tendered"]').fill(String(tendered));
}

export async function refundBy(page: Page, method: 'Cash' | 'Card' | 'Mobile Banking', reference?: string): Promise<void> {
  await page.locator('fieldset').filter({ hasText: 'Refund method' }).getByLabel(method, { exact: false }).check();
  if (reference) {
    await page.locator('input[name="refundReference"]').fill(reference);
    await page.locator('input[name="refundConfirmed"]').check();
  }
}

// Supervisor approvals are rate-limited to 5 per minute per IP, and every browser test shares 127.0.0.1.
const approvalCalls: number[] = [];
export async function approvalSlot(page: Page): Promise<void> {
  const recent = approvalCalls.filter(at => Date.now() - at < 61_000);
  if (recent.length >= 4) await page.waitForTimeout(61_000 - (Date.now() - recent[0]) + 500);
  approvalCalls.push(Date.now());
}

export async function approve(page: Page, supervisorEmail: string, password: string, reason = 'UAT supervisor approval'): Promise<void> {
  await approvalSlot(page);
  await page.getByTestId('open-approvals').click();
  const dialog = page.getByRole('dialog', { name: 'Supervisor approval' });
  await dialog.locator('input[name="supervisorLogin"]').fill(supervisorEmail);
  await dialog.locator('input[name="supervisorPassword"]').fill(password);
  for (const box of await dialog.locator('textarea').all()) await box.fill(reason);
  await dialog.getByRole('button', { name: 'Approve' }).click();
}

export async function confirmExchange(page: Page): Promise<string> {
  await page.getByTestId('confirm-exchange').click();
  const number = page.getByTestId('exchange-number');
  await expect(number).toBeVisible({ timeout: 30_000 });
  return (await number.innerText()).trim();
}

export function money(text: string): number {
  return Number(text.replace(/[^\d.-]/g, ''));
}

export async function totalsValue(page: Page, label: string): Promise<number> {
  const row = page.getByTestId('preview-totals').locator('div').filter({ has: page.locator('dt', { hasText: label }) }).first();
  return money(await row.locator('dd').innerText());
}

export interface QuickExchange {
  invoices: string[];
  returns: { product: string; quantity?: number }[];
  replacements: { barcode: string; quantity?: number }[];
}

/** Drives the workflow from the invoice step to the server Review step (does not settle or confirm). */
export async function prepareExchange(page: Page, plan: QuickExchange): Promise<void> {
  await openExchange(page);
  for (const invoice of plan.invoices) {
    await findInvoice(page, invoice);
    await expect(page.locator('ul[aria-label="Selected invoices"]')).toContainText(invoice);
  }
  await next(page);
  for (const item of plan.returns) await returnItem(page, item.product, item.quantity);
  await next(page);
  for (const item of plan.replacements) await addReplacement(page, item.barcode, item.quantity);
  await next(page);
  await chooseReason(page);
  await review(page);
}

/** Goes to Settle & confirm from Review. */
export async function toSettle(page: Page): Promise<void> {
  await next(page);
  await expect(page.getByTestId('confirm-exchange')).toBeVisible();
}
