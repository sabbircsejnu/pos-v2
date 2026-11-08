import { describe, expect, it } from 'vitest';
import { readExchangeError } from './exchange-errors';

function apiError(status: number, body: unknown) {
  return { status, originalError: { error: body } };
}

describe('readExchangeError', () => {
  it('shows the server reason for an inactive terminal instead of the generic cash session text', () => {
    const info = readExchangeError(apiError(422, {
      errorCode: 'EXCHANGE_SESSION_REQUIRED',
      message: 'The selected terminal is not active at this outlet. Select an active terminal and review the exchange again.'
    }));
    expect(info.code).toBe('EXCHANGE_SESSION_REQUIRED');
    expect(info.message).toContain('terminal is not active');
  });

  it('shows why a source is ineligible, e.g. a reversed exchange (browser UAT DEF-04, I-12 item 3)', () => {
    const info = readExchangeError(apiError(422, {
      errorCode: 'EXCHANGE_SOURCE_INELIGIBLE',
      message: 'That exchange was reversed, so its items cannot be exchanged again.'
    }));
    expect(info.message).toBe('That exchange was reversed, so its items cannot be exchanged again.');
  });

  it('falls back to the fixed text when the server gives no message', () => {
    expect(readExchangeError(apiError(422, { errorCode: 'EXCHANGE_SESSION_REQUIRED' })).message).toContain('active cash session');
  });

  it('explains a supervisor lockout', () => {
    const info = readExchangeError(apiError(429, { errorCode: 'EXCHANGE_APPROVAL_LOCKED', message: 'x' }));
    expect(info.message).toContain('Too many failed supervisor sign-ins');
  });

  it('explains a rate-limited request that has no body', () => {
    expect(readExchangeError({ status: 429, originalError: { error: null } }).message).toContain('Too many attempts');
  });
});
