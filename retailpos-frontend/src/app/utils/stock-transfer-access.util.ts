import { AuthorizedOutletsDto } from '../models/report.model';
import { StockTransferDto } from '../models/stock-transfer.model';

function matchesLocation(locationType: string, locationId: number, expectedType?: string | null, expectedId?: number | null): boolean {
  return !!expectedType
    && !!expectedId
    && locationId === expectedId
    && locationType.toLowerCase() === expectedType.toLowerCase();
}

function isInAuthorizedSourceLocations(auth: AuthorizedOutletsDto, locationType: string, locationId: number): boolean {
  const normalizedType = locationType.toLowerCase();

  if (normalizedType === 'outlet') {
    return auth.outlets.some(location => location.id === locationId);
  }

  if (normalizedType === 'warehouse') {
    return auth.warehouses.some(location => location.id === locationId);
  }

  return false;
}

export function canManageTransferFromSource(transfer: StockTransferDto, auth: AuthorizedOutletsDto | null): boolean {
  if (!auth) {
    return false;
  }

  return isInAuthorizedSourceLocations(auth, transfer.fromLocationType, transfer.fromLocationId);
}

/**
 * `auth` supplies the user's default location (the source pin); `sourceAuth` is the StockTransferSource
 * policy response (Step 5 assignments) and defaults to `auth`. Both must allow the action, as on the API.
 */
export function canDispatchTransferFromSource(
  transfer: StockTransferDto,
  auth: AuthorizedOutletsDto | null,
  hasCrossLocationSourcePermission: boolean,
  sourceAuth: AuthorizedOutletsDto | null = auth
): boolean {
  if (!auth || !sourceAuth) {
    return false;
  }

  if (!isInAuthorizedSourceLocations(sourceAuth, transfer.fromLocationType, transfer.fromLocationId)) {
    return false;
  }

  if (hasCrossLocationSourcePermission) {
    return true;
  }

  return matchesLocation(
    transfer.fromLocationType,
    transfer.fromLocationId,
    auth.defaultLocationType,
    auth.defaultLocationId
  );
}

export function canReceiveTransferAtDestination(transfer: StockTransferDto, auth: AuthorizedOutletsDto | null): boolean {
  if (!auth) {
    return false;
  }

  return matchesLocation(
    transfer.toLocationType,
    transfer.toLocationId,
    auth.defaultLocationType,
    auth.defaultLocationId
  );
}
