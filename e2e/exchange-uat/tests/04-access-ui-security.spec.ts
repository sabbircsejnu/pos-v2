import { createSale } from '../support/api';
import { barcode, outletA, outletB, price, terminalA, terminalB, uatPassword, users } from '../support/env';
import { approvalSlot, as, expect, openExchange, prepareExchange, snap, test, toSettle } from '../support/ui';

test.describe('Authorization (U-70, U-52)', () => {
  test('U-70 a user without pos.exchange.create sees access denied and stays signed in (403 never logs out)', async ({ browser }) => {
    const context = await browser.newContext(as('accounts'));
    const page = await context.newPage();
    await page.goto('/pos/exchange');
    await expect(page.getByRole('heading', { name: 'Product Exchange' })).toHaveCount(0);
    await expect(page).not.toHaveURL(/\/login/);
    await snap(page, 'U-70-access-denied');
    await context.close();
  });
});

test.describe('Policy settings (U-72, DN-01)', () => {
  test.use(as('owner'));

  test('required phone verification is shown as unavailable', async ({ page }) => {
    await page.goto('/settings/exchange');
    await expect(page.getByText(/required/i).first()).toBeVisible();
    const required = page.locator('option[value="required"], input[value="required"]');
    if (await required.count()) await expect(required.first()).toBeDisabled();
    await expect(page.getByText(/not available|unavailable/i).first()).toBeVisible();
    await snap(page, 'U-72-policy-settings');
  });
});

test.describe('Metrics (U-62)', () => {
  test.use(as('owner'));

  test('the exchange history page shows today\'s metrics', async ({ page }) => {
    await page.goto('/exchanges');
    await expect(page.getByTestId('exchange-metrics')).toBeVisible();
    await snap(page, 'U-62-metrics');
  });
});

test.describe('Responsive (U-73) and offline (U-74)', () => {
  test.use(as('salesA'));

  for (const width of [390, 768, 1440]) {
    test(`exchange workflow and history at ${width}px have no horizontal page scroll`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await openExchange(page);
      await snap(page, `U-73-exchange-${width}`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      await page.goto('/exchanges');
      await expect(page.getByRole('heading', { name: 'Exchanges' })).toBeVisible();
      await snap(page, `U-73-history-${width}`);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
    });
  }

  test('U-74 going offline blocks Confirm and keeps the reviewed exchange', async ({ page, context }) => {
    const sale = await createSale('salesA', outletA(), terminalA(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }]);
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('SHIRT') }] });
    await toSettle(page);
    await context.setOffline(true);
    await page.evaluate(() => window.dispatchEvent(new Event('offline')));
    await expect(page.getByTestId('connection-state')).toHaveText('Offline');
    await expect(page.getByTestId('confirm-exchange')).toBeDisabled();
    await snap(page, 'U-74-offline');
    await context.setOffline(false);
    await page.evaluate(() => window.dispatchEvent(new Event('online')));
    await expect(page.getByTestId('connection-state')).toHaveText('Online');
    await expect(page.getByTestId('confirm-exchange')).toBeEnabled();
  });
});

test.describe('Supervisor sign-in lockout (U-23, U-24, L1b)', () => {
  // Sales B works the till at outlet B and the real login is the accounts user: both are locked for 15 minutes
  // afterwards (L1b) and no other test depends on them. Set UAT_SKIP_LOCKOUT=1 to re-run within those 15 minutes.
  test.use(as('salesB'));
  test.skip(process.env.UAT_SKIP_LOCKOUT === '1', 'Skipped on request (lockout window still active)');

  test('five wrong passwords lock the login; an unknown login gets identical answers', async ({ page }) => {
    test.setTimeout(600_000); // 12 attempts paced for the 5-per-minute approval rate limit
    const sale = await createSale('owner', outletB(), terminalB(), [{ code: 'SHIRT', quantity: 1, unitPrice: price.SHIRT }],
      { salesDate: `${new Date(Date.now() - 9 * 86_400_000).toISOString().slice(0, 10)}T10:00:00` });
    await prepareExchange(page, { invoices: [sale.number], returns: [{ product: 'UAT Shirt', quantity: 1 }], replacements: [{ barcode: barcode('SHIRT') }] });
    await toSettle(page);

    const answers: Record<string, string[]> = {};
    for (const login of [`nobody-${Date.now()}@exchange-uat.test`, users.accounts]) {
      answers[login] = [];
      for (let attempt = 0; attempt < 6; attempt++) {
        await approvalSlot(page);
        await page.getByTestId('open-approvals').click();
        const dialog = page.getByRole('dialog', { name: 'Supervisor approval' });
        await dialog.locator('input[name="supervisorLogin"]').fill(login);
        await dialog.locator('input[name="supervisorPassword"]').fill(attempt < 5 ? 'wrong-password' : uatPassword());
        for (const box of await dialog.locator('textarea').all()) await box.fill('Lockout check');
        await dialog.getByRole('button', { name: 'Approve' }).click();
        const error = dialog.getByTestId('approval-error');
        await expect(error).toBeVisible();
        answers[login].push((await error.innerText()).trim());
        await dialog.getByRole('button', { name: 'Cancel' }).click();
      }
    }
    const [unknown, real] = Object.values(answers);
    expect(unknown.slice(0, 5).every(text => text === unknown[0])).toBe(true);
    expect(unknown[5]).toMatch(/Too many failed supervisor sign-ins/);
    expect(real[5]).toBe(unknown[5]);
    expect(real[0]).toBe(unknown[0]);
    await snap(page, 'U-23-lockout');
  });
});
