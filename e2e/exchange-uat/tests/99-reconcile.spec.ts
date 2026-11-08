import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { test, expect } from '@playwright/test';
import { tool } from '../support/api';
import { EVIDENCE } from '../support/env';

// Runs last: read-only reconciliation of every exchange and reversal in the UAT database after the browser run.
test('database reconciliation: journals balanced, ledger = inventory = recorded COGS, entitlements and drawer cash', async () => {
  let output = '';
  try {
    output = tool('reconcile');
  } catch (error: any) {
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
  }
  writeFileSync(resolve(EVIDENCE, 'reconciliation.txt'), output);
  expect(output).toContain('RECONCILIATION OK');
});
