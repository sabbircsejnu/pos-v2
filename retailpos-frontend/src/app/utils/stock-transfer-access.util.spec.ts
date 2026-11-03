import { describe, expect, it } from 'vitest';
import { AuthorizedOutletsDto } from '../models/report.model';
import { StockTransferDto } from '../models/stock-transfer.model';
import { canDispatchTransferFromSource, canManageTransferFromSource, canReceiveTransferAtDestination } from './stock-transfer-access.util';

const authorizedAccess: AuthorizedOutletsDto = {
  outlets: [{ id: 11, name: 'Source Outlet', type: 'outlet' }],
  warehouses: [{ id: 21, name: 'Destination Warehouse', type: 'warehouse' }],
  destinationOutlets: [{ id: 11, name: 'Source Outlet', type: 'outlet' }],
  destinationWarehouses: [{ id: 21, name: 'Destination Warehouse', type: 'warehouse' }],
  defaultLocationId: 21,
  defaultLocationType: 'warehouse',
  isBusinessOwner: false,
  accessScope: 'assigned_only'
};

const transfer: StockTransferDto = {
  id: 1,
  transferType: 'direct',
  fromLocationId: 11,
  fromLocationType: 'outlet',
  fromLocationName: 'Source Outlet',
  toLocationId: 21,
  toLocationType: 'warehouse',
  toLocationName: 'Destination Warehouse',
  transferDate: '2026-07-25',
  status: 'approved',
  createdAt: '2026-07-25T00:00:00Z',
  items: []
};

describe('stock-transfer-access util', () => {
  it('allows source-managed actions but blocks dispatch without default-source context', () => {
    expect(canManageTransferFromSource(transfer, authorizedAccess)).toBe(true);
    expect(canDispatchTransferFromSource(transfer, authorizedAccess, false)).toBe(false);
  });

  it('allows destination receive only for matching default location', () => {
    expect(canReceiveTransferAtDestination(transfer, authorizedAccess)).toBe(true);
    expect(canReceiveTransferAtDestination({ ...transfer, toLocationId: 22 }, authorizedAccess)).toBe(false);
  });

  it('allows cross-location dispatch when special source permission applies', () => {
    expect(canDispatchTransferFromSource(transfer, authorizedAccess, true)).toBe(true);
  });

  it('requires the source to be a Step 5 source location even when the default location matches', () => {
    const defaultMatchesSource: AuthorizedOutletsDto = { ...authorizedAccess, defaultLocationId: 11, defaultLocationType: 'outlet' };
    const sourceAccess: AuthorizedOutletsDto = { ...authorizedAccess, outlets: [], warehouses: [] };

    expect(canDispatchTransferFromSource(transfer, defaultMatchesSource, false, sourceAccess)).toBe(false);
    expect(canDispatchTransferFromSource(transfer, defaultMatchesSource, true, sourceAccess)).toBe(false);
    expect(canManageTransferFromSource(transfer, sourceAccess)).toBe(false);
    expect(canDispatchTransferFromSource(transfer, defaultMatchesSource, false, authorizedAccess)).toBe(true);
    expect(canDispatchTransferFromSource(transfer, defaultMatchesSource, false, null)).toBe(false);
  });
});
