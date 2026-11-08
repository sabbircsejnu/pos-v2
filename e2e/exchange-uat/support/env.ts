import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const uatDir = resolve(import.meta.dirname, '..', '.uat');

export const API = process.env.UAT_API_URL ?? 'http://127.0.0.1:5105/api';
export const EVIDENCE = resolve(import.meta.dirname, '..', 'evidence');

interface Ids {
  businessId: number;
  outlets: Record<string, number>;
  terminals: Record<string, number>;
  variants: Record<string, number>;
  users: Record<string, number>;
  customerId: number;
  supplierId: number;
}

export const ids: Ids = JSON.parse(readFileSync(resolve(uatDir, 'ids.json'), 'utf8'));

/** The synthetic password of the synthetic UAT users (git-ignored local file; never logged). */
export function uatPassword(): string {
  return JSON.parse(readFileSync(resolve(uatDir, 'credentials.json'), 'utf8')).password;
}

export const users = {
  owner: 'uat.owner@exchange-uat.test',
  managerA: 'uat.manager.a@exchange-uat.test',
  salesA: 'uat.sales.a@exchange-uat.test',
  accounts: 'uat.accounts@exchange-uat.test',
  managerB: 'uat.manager.b@exchange-uat.test',
  salesB: 'uat.sales.b@exchange-uat.test'
} as const;
export type UatUser = keyof typeof users;

export const outletA = () => ids.outlets['UAT Outlet A'];
export const outletB = () => ids.outlets['UAT Outlet B'];
export const terminalA = () => ids.terminals['UATA-T1'];
export const terminalB = () => ids.terminals['UATB-T1'];
export const variant = (code: 'SHIRT' | 'JACKET' | 'CAP' | 'COAT' | 'WATCH') => ids.variants[`UAT-${code}-STD`];
export const barcode = (code: 'SHIRT' | 'JACKET' | 'CAP' | 'COAT' | 'WATCH') => `UAT-${code}-STD`;
export const price = { SHIRT: 100, JACKET: 120, CAP: 50, COAT: 300, WATCH: 600 } as const;
