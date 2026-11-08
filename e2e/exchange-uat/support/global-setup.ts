import { existsSync, readFileSync } from 'node:fs';
import { ensurePolicy, ensureSessions, login, saveSession, sessionPath, topUpStock } from './api';
import { users, type UatUser } from './env';

function stillValid(user: UatUser): boolean {
  // UAT_RELOGIN=1 forces fresh sign-ins (for example after role permissions changed; tokens carry permissions).
  if (process.env.UAT_RELOGIN === '1' || !existsSync(sessionPath(user))) return false;
  try {
    const { token } = JSON.parse(readFileSync(sessionPath(user), 'utf8'));
    const payload = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8'));
    return typeof payload.exp === 'number' && payload.exp * 1000 > Date.now() + 30 * 60_000;
  } catch {
    return false;
  }
}

/**
 * Prepares the persistent synthetic UAT database for a browser run (idempotent; never resets anything):
 * signs every synthetic user in through the real login endpoint (paced for its 5-per-minute limit), saves the exchange
 * policy if it differs, opens today's cash sessions and receives synthetic stock.
 */
export default async function globalSetup(): Promise<void> {
  let logins = 0;
  for (const user of Object.keys(users) as UatUser[]) {
    if (stillValid(user)) continue;
    if (logins > 0 && logins % 4 === 0) await new Promise(done => setTimeout(done, 61_000));
    saveSession(user, await login(user));
    logins++;
  }
  await ensurePolicy();
  await ensureSessions();
  await topUpStock();
}
