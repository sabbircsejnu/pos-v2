import { readApiErrorBody } from './pos-sale-errors';

export const CATALOGUE_OFFLINE_MESSAGE =
  'Unable to reach the server to load products. Check the connection and try again.';
export const CATALOGUE_BUSINESS_REQUIRED_MESSAGE =
  'Select a Business before using POS Checkout, then reload the product list.';
export const CATALOGUE_FORBIDDEN_MESSAGE =
  'Products could not be loaded: your account is not permitted to sell at this outlet.';
export const CATALOGUE_FAILED_MESSAGE = 'Products could not be loaded. Please try again.';

/**
 * Describes why a POS catalogue read failed, so a lookup failure is never shown as a generic
 * permission error when the cause is connectivity, a missing Business context or an outlet scope.
 */
export function posCatalogueErrorMessage(error: any): string {
  const status = Number(error?.status ?? error?.originalError?.status ?? -1);
  // Only the server's body: the interceptor's own `error` wrapper carries its generic permission text.
  const body = (error?.originalError ? readApiErrorBody({ originalError: error.originalError }) : readApiErrorBody(error)) as
    { errorCode?: string; message?: string } | null;
  const serverMessage = typeof body?.message === 'string' && body.message.trim() ? body.message.trim() : null;

  if (status === 0) {
    return CATALOGUE_OFFLINE_MESSAGE;
  }
  if (body?.errorCode === 'business_context_required') {
    return CATALOGUE_BUSINESS_REQUIRED_MESSAGE;
  }
  if (status === 403) {
    // The POS endpoint explains outlet-scope refusals; a bare policy 403 means the POS sale permission is missing.
    return serverMessage ? `Products could not be loaded: ${serverMessage}` : CATALOGUE_FORBIDDEN_MESSAGE;
  }
  if (status === 400 && serverMessage) {
    return `Products could not be loaded: ${serverMessage}`;
  }
  return CATALOGUE_FAILED_MESSAGE;
}
