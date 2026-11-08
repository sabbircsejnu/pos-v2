import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { API, ids, outletA, outletB, terminalA, terminalB, uatPassword, users, variant, type UatUser } from './env';

const authDir = resolve(import.meta.dirname, '..', '.uat', 'auth');

export interface Session { token: string; refreshToken: string; user: Record<string, unknown> }

export class ApiError extends Error {
  constructor(readonly status: number, readonly body: string) {
    super(`HTTP ${status}: ${body.slice(0, 400)}`);
  }
  get code(): string {
    try { return JSON.parse(this.body).errorCode ?? ''; } catch { return ''; }
  }
}

export async function call<T = any>(token: string | null, method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const text = await response.text();
  if (!response.ok) throw new ApiError(response.status, text);
  if (!text) return undefined as T;
  const json = JSON.parse(text);
  return (json && typeof json === 'object' && 'data' in json && 'success' in json ? json.data : json) as T;
}

/** Signs in through the real login endpoint (5 per minute per IP: callers pace themselves). */
export async function login(user: UatUser): Promise<Session> {
  return call<Session>(null, 'POST', '/auth/login', { email: users[user], password: uatPassword(), rememberMe: false });
}

export function saveSession(user: UatUser, session: Session): void {
  mkdirSync(authDir, { recursive: true });
  writeFileSync(sessionPath(user), JSON.stringify(session));
  writeFileSync(statePath(user), JSON.stringify({
    cookies: [],
    origins: [{
      origin: 'http://127.0.0.1:4200',
      localStorage: [
        { name: 'auth_token', value: session.token },
        { name: 'refresh_token', value: session.refreshToken },
        { name: 'current_user', value: JSON.stringify(session.user) }
      ]
    }]
  }));
}

export const sessionPath = (user: UatUser) => resolve(authDir, `${user}.session.json`);
export const statePath = (user: UatUser) => resolve(authDir, `${user}.state.json`);
export function token(user: UatUser): string {
  if (!existsSync(sessionPath(user))) throw new Error(`No session for ${user}; run global setup.`);
  return JSON.parse(readFileSync(sessionPath(user), 'utf8')).token;
}

export const unique = (prefix: string) => `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}`;

// ---- Environment preparation (idempotent) ----

export async function ensurePolicy(): Promise<void> {
  const owner = token('owner');
  const current = await call<any>(owner, 'GET', '/pos/exchanges/policy');
  const wanted = {
    isEnabled: true, allowLowerPriceExchange: true, exchangeWindowDays: 7, allowReplacementReExchange: true,
    replacementWindowDays: 7, maxExchangeCountPerLineage: 2, phoneVerificationMode: 'optional',
    highValueApprovalThreshold: 500, negativeTargetAdjustmentMode: 'track_only'
  };
  if (current?.isConfigured
    && current.isEnabled && current.exchangeWindowDays === 7 && Number(current.highValueApprovalThreshold) === 500
    && current.maxExchangeCountPerLineage === 2 && current.allowReplacementReExchange && current.allowLowerPriceExchange) return;
  await call(owner, 'PUT', '/pos/exchanges/policy', { expectedVersionNo: current?.isConfigured ? current.versionNo : null, ...wanted });
}

export async function activeSession(user: UatUser, outletId: number, terminalId: number): Promise<any | null> {
  return call<any>(token(user), 'GET', `/cash-sessions/active?outletId=${outletId}&terminalId=${terminalId}`);
}

/** Opens today's session for each UAT terminal if none is open; closes a session that is from an earlier business day. */
export async function ensureSessions(): Promise<void> {
  const owner = token('owner');
  for (const [outletId, terminalId] of [[outletA(), terminalA()], [outletB(), terminalB()]]) {
    let active = await activeSession('owner', outletId, terminalId);
    if (active) {
      const openedHoursAgo = (Date.now() - Date.parse(active.openedAt)) / 3_600_000;
      if (openedHoursAgo < 16) continue;
      const summary = await call<any>(owner, 'GET', `/cash-sessions/${active.id}/summary`);
      await call(owner, 'POST', `/cash-sessions/${active.id}/close`, {
        actualClosingCashAmount: summary.expectedCash ?? summary.expectedClosingCash ?? 0,
        closingNotes: 'UAT: closed by the browser UAT setup before opening a new business day'
      });
      active = null;
    }
    await call(owner, 'POST', '/cash-sessions/open', {
      outletId, terminalId, openingCashAmount: 5000, openingNotes: 'UAT opening float (synthetic)', idempotencyKey: unique('open')
    });
  }
}

/** Receives synthetic stock at both outlets so every run has goods to exchange. */
export async function topUpStock(quantity = 20): Promise<void> {
  const owner = token('owner');
  const costs: Record<string, number> = { SHIRT: 40, JACKET: 50, CAP: 20, COAT: 120, WATCH: 250 };
  for (const locationId of [outletA(), outletB()]) {
    await call(owner, 'POST', '/purchase-orders/purchase-receive', {
      supplierId: ids.supplierId, locationType: 'outlet', locationId, orderDate: new Date().toISOString(), idempotencyKey: unique('grn'),
      items: Object.entries(costs).map(([code, unitPrice]) => ({ variantId: variant(code as any), quantity, unitPrice, discount: 0, tax: 0 }))
    });
  }
}

export interface SaleLine { code: 'SHIRT' | 'JACKET' | 'CAP' | 'COAT' | 'WATCH'; quantity: number; unitPrice: number }
export interface CreatedSale { id: number; number: string; items: { id: number; variantId: number }[] }

/** A real POS sale (card payment) through the checkout API, so the source invoice carries full price/VAT/cost evidence. */
export async function createSale(user: UatUser, outletId: number, terminalId: number, lines: SaleLine[],
  options: { customerId?: number; salesDate?: string } = {}): Promise<CreatedSale> {
  const net = lines.reduce((sum, line) => sum + line.quantity * line.unitPrice, 0);
  const tax = Math.round(net * 0.10 * 100) / 100; // the synthetic business charges 10% VAT on top (POS settings)
  const total = Math.round((net + tax) * 100) / 100;
  const sale = await call<any>(token(user), 'POST', '/sales', {
    outletId, terminalId, customerId: options.customerId ?? null, discount: 0, tax, paymentMethod: 'card',
    idempotencyKey: unique('sale'), salesDate: options.salesDate ?? null,
    payments: [{ method: 'card', amount: total }],
    items: lines.map(line => ({ variantId: variant(line.code), quantity: line.quantity, unitPrice: line.unitPrice, discountAmount: 0 }))
  });
  return {
    id: sale.id,
    number: sale.saleNumber ?? sale.invoiceNumber,
    items: (sale.items ?? []).map((item: any) => ({ id: item.id, variantId: item.variantId }))
  };
}

const toolProject = resolve(import.meta.dirname, '..', '..', '..', 'tools', 'RetailPOS.ExchangeUat');

/** Read-only database query through the UAT tool (Debug build, so it never locks the running API's Release build). */
export function tool(...args: string[]): string {
  return execFileSync('dotnet', ['run', '--project', toolProject, '-c', 'Debug', '--no-build', '--', ...args], { encoding: 'utf8' });
}

/** On-hand quantity of a synthetic variant at an outlet, read from the UAT database. */
export function onHand(outletId: number, code: SaleLine['code']): number {
  return Number(tool('stock', String(variant(code)), String(outletId)).trim().split(/\r?\n/).pop());
}
