import {
  CATALOGUE_BUSINESS_REQUIRED_MESSAGE,
  CATALOGUE_FAILED_MESSAGE,
  CATALOGUE_FORBIDDEN_MESSAGE,
  CATALOGUE_OFFLINE_MESSAGE,
  posCatalogueErrorMessage
} from './pos-lookup-errors';

const GENERIC_FORBIDDEN = 'You do not have permission to perform this action.';

// Error shape produced by the HTTP error interceptor: { status, message, error: <interceptor wrapper>, originalError }.
const intercepted = (status: number, body: unknown) => ({
  status,
  message: status === 403 ? GENERIC_FORBIDDEN : 'x',
  error: { message: status === 403 ? GENERIC_FORBIDDEN : 'x' },
  originalError: { status, error: body }
});

describe('posCatalogueErrorMessage', () => {
  it('reports connectivity failures as such', () => {
    expect(posCatalogueErrorMessage(intercepted(0, null))).toBe(CATALOGUE_OFFLINE_MESSAGE);
  });

  it('reports a missing Business context instead of a permission problem', () => {
    const error = intercepted(400, { errorCode: 'business_context_required', message: 'A validated Business scope is required.' });
    expect(posCatalogueErrorMessage(error)).toBe(CATALOGUE_BUSINESS_REQUIRED_MESSAGE);
  });

  it('passes through the server explanation of an outlet-scope refusal', () => {
    const error = intercepted(403, { success: false, message: 'You are not assigned to outlet 5002.' });
    expect(posCatalogueErrorMessage(error)).toBe('Products could not be loaded: You are not assigned to outlet 5002.');
  });

  it('never echoes the interceptor generic permission text for a bare policy 403', () => {
    const message = posCatalogueErrorMessage(intercepted(403, null));
    expect(message).toBe(CATALOGUE_FORBIDDEN_MESSAGE);
    expect(message).not.toContain(GENERIC_FORBIDDEN);
  });

  it('explains validation failures and falls back to a retry hint otherwise', () => {
    expect(posCatalogueErrorMessage(intercepted(400, { message: 'limit must be between 1 and 500.' })))
      .toBe('Products could not be loaded: limit must be between 1 and 500.');
    expect(posCatalogueErrorMessage(intercepted(500, { message: 'boom' }))).toBe(CATALOGUE_FAILED_MESSAGE);
    expect(posCatalogueErrorMessage(undefined)).toBe(CATALOGUE_FAILED_MESSAGE);
  });
});
