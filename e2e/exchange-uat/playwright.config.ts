import { defineConfig } from '@playwright/test';

// Browser UAT for Product Exchange against the persistent synthetic database iranimart_pos_v2_exchange_uat.
// Prerequisites (see Docs/08_Sales_And_POS/Exchange/IRANIMART_POS_V2_EXCHANGE_UAT_ENVIRONMENT.md):
//   UAT API on http://127.0.0.1:5105 and UAT web on http://127.0.0.1:4200.
// Uses the installed Microsoft Edge (channel msedge); no browser download.
export default defineConfig({
  testDir: './tests',
  // One shared persistent database and one cash drawer per outlet: run serially, in file order.
  workers: 1,
  fullyParallel: false,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  retries: 0,
  globalSetup: './support/global-setup.ts',
  outputDir: './test-results',
  reporter: [
    ['list'],
    ['json', { outputFile: './evidence/results.json' }],
    ['html', { outputFolder: './playwright-report', open: 'never' }]
  ],
  use: {
    baseURL: 'http://127.0.0.1:4200',
    channel: 'msedge',
    headless: true,
    viewport: { width: 1440, height: 900 },
    screenshot: 'on',
    trace: 'retain-on-failure',
    acceptDownloads: true
  }
});
