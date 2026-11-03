import { Location } from '@angular/common';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { StockTransferService } from '../../services/stock-transfer.service';
import { UnsavedChangesService } from '../../services/unsaved-changes.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { TransferCreateComponent } from './transfer-create.component';

describe('TransferCreateComponent location selection', () => {
  const outlets = [{ id: 1, name: 'Outlet A' }, { id: 2, name: 'Outlet B' }];
  const warehouses = [{ id: 1, name: 'Warehouse A' }, { id: 2, name: 'Warehouse B' }];
  let fixture: ComponentFixture<TransferCreateComponent>;
  let component: TransferCreateComponent;
  let canEditSource: boolean;
  const accessData = () => ({
    outlets,
    warehouses,
    destinationOutlets: [...outlets, { id: 3, name: 'Destination only' }],
    destinationWarehouses: warehouses,
    defaultLocationType: 'outlet',
    defaultLocationId: 1
  });
  const loadAccess = vi.fn();

  beforeEach(async () => {
    canEditSource = true;
    loadAccess.mockReset();
    loadAccess.mockImplementation(() => of({ data: accessData() }));
    await TestBed.configureTestingModule({
      imports: [TransferCreateComponent],
      providers: [
        { provide: StockTransferService, useValue: {} },
        { provide: UserOutletAccessService, useValue: { load: loadAccess } },
        { provide: AuthService, useValue: {
          hasPermission: (permission: string) =>
            permission === 'stock_transfers.transfer_from_any_location' && canEditSource
        } },
        { provide: Router, useValue: {} },
        { provide: Location, useValue: {} },
        { provide: AlertService, useValue: {} },
        { provide: ErrorHandlerService, useValue: {} },
        { provide: ProductImageService, useValue: {} },
        { provide: UnsavedChangesService, useValue: {} }
      ]
    }).overrideComponent(TransferCreateComponent, {
      remove: { imports: [VariantSearchPickerComponent] },
      add: { schemas: [NO_ERRORS_SCHEMA] }
    }).compileComponents();
    fixture = TestBed.createComponent(TransferCreateComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function optionLabels(name: string): string[] {
    const select = fixture.nativeElement.querySelector(`select[name="${name}"]`) as HTMLSelectElement;
    return Array.from(select.options, option => option.text);
  }

  it('excludes the default source from rendered destination options without widening authorization', () => {
    expect(optionLabels('toLocationId')).toEqual([
      '-- Select Destination --', 'Outlet B', 'Destination only'
    ]);
    expect(component.getFromLocations()).toEqual(outlets);
    expect(component.destinationOutlets()).toHaveLength(3);
  });

  it('updates both dropdowns as locations change and restores previously excluded options', () => {
    component.onToLocationChange(2);
    fixture.detectChanges();
    expect(optionLabels('fromLocationId')).toEqual(['-- Select Source --', 'Outlet A']);

    component.onToLocationChange(3);
    component.onFromLocationChange(2);
    fixture.detectChanges();
    expect(optionLabels('fromLocationId')).toEqual(['-- Select Source --', 'Outlet A', 'Outlet B']);
    expect(optionLabels('toLocationId')).toEqual([
      '-- Select Destination --', 'Outlet A', 'Destination only'
    ]);
    expect(component.toLocationId()).toBe(3);
  });

  it('clears a destination that conflicts with a changed source', () => {
    component.onToLocationChange(2);
    component.onFromLocationChange(2);
    expect(component.fromLocationId()).toBe(2);
    expect(component.toLocationId()).toBeNull();
    expect(component.canSearchProducts()).toBe(false);
  });

  it('updates destination options through the source dropdown change event', async () => {
    await fixture.whenStable();
    const select = fixture.nativeElement.querySelector('select[name="fromLocationId"]') as HTMLSelectElement;
    const option = Array.from(select.options).find(option => option.text === 'Outlet B')!;
    select.value = option.value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.fromLocationId()).toBe(2);
    expect(optionLabels('toLocationId')).toEqual([
      '-- Select Destination --', 'Outlet A', 'Destination only'
    ]);
  });

  it('clears an editable source that conflicts with a changed destination', () => {
    component.onToLocationChange(1);
    expect(component.fromLocationId()).toBeNull();
    expect(component.toLocationId()).toBe(1);
  });

  it('keeps matching numeric IDs available across different location types', () => {
    component.toLocationType.set('warehouse');
    component.onToTypeChange();
    component.onToLocationChange(1);
    fixture.detectChanges();
    expect(component.fromLocationId()).toBe(1);
    expect(component.toLocationId()).toBe(1);
    expect(component.sameLocationSelected()).toBe(false);
    expect(component.getToLocations()).toEqual(warehouses);
    expect(component.getFromLocations()).toEqual(outlets);
    expect(optionLabels('toLocationId')).toContain('Warehouse A');
  });

  it('applies exclusions to warehouses after location type changes', () => {
    component.fromLocationType.set('warehouse');
    component.onFromTypeChange();
    component.onFromLocationChange(1);
    component.toLocationType.set('warehouse');
    component.onToTypeChange();
    component.onToLocationChange(2);
    fixture.detectChanges();
    expect(optionLabels('fromLocationId')).toEqual(['-- Select Source --', 'Warehouse A']);
    expect(optionLabels('toLocationId')).toEqual(['-- Select Destination --', 'Warehouse B']);

    component.toLocationType.set('outlet');
    component.onToTypeChange();
    expect(component.toLocationId()).toBeNull();
    expect(component.getFromLocations()).toEqual(warehouses);
    expect(component.getToLocations()).toHaveLength(3);
  });

  it('retains the read-only default source and excludes it from destination options', () => {
    fixture.destroy();
    canEditSource = false;
    fixture = TestBed.createComponent(TransferCreateComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('select[name="fromLocationId"]')).toBeNull();
    expect(optionLabels('toLocationId')).not.toContain('Outlet A');
    component.onFromLocationChange(2);
    component.onToLocationChange(1);
    expect(component.fromLocationId()).toBe(1);
    expect(component.toLocationId()).toBeNull();
    expect(component.getFromLocationName()).toBe('Outlet A');
  });

  it('clears a conflicting destination when the default source is resolved', () => {
    component.fromLocationId.set(null);
    component.onToLocationChange(1);
    component.loadSupportingData();
    expect(component.fromLocationId()).toBe(1);
    expect(component.toLocationId()).toBeNull();
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('loads Step 5 source locations with the StockTransferSource policy and the default separately', () => {
    expect(loadAccess).toHaveBeenCalledWith();
    expect(loadAccess).toHaveBeenCalledWith('StockTransferSource');
    expect(loadAccess).not.toHaveBeenCalledWith('InventoryStockView');
  });

  it('does not preselect a default source that is outside the Step 5 source locations', () => {
    loadAccess.mockImplementation((policy?: string) => of({ data: {
      ...accessData(),
      outlets: policy === 'StockTransferSource' ? [{ id: 2, name: 'Outlet B' }] : outlets,
      defaultLocationId: 1
    } }));
    component.fromLocationId.set(null);
    component.loadSupportingData();
    expect(component.fromLocationId()).toBeNull();
    expect(component.getFromLocations()).toEqual([{ id: 2, name: 'Outlet B' }]);
    expect(component.destinationOutlets()).toHaveLength(3);
  });

  it.each([true, false])('preserves source change confirmation (confirmed: %s)', confirmed => {
    component.onToLocationChange(2);
    component.lineItems.set([{
      variantId: 10,
      variantSku: 'SKU-10',
      productName: 'Product',
      availableStock: 5,
      destinationStock: 4,
      requestedQuantity: 1,
      transferQuantity: 1
    }]);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(confirmed);
    try {
      component.onFromLocationChange(2);
      expect(confirm).toHaveBeenCalledOnce();
      expect(component.fromLocationId()).toBe(confirmed ? 2 : 1);
      expect(component.toLocationId()).toBe(confirmed ? null : 2);
      expect(component.lineItems()).toHaveLength(confirmed ? 0 : 1);
    } finally {
      confirm.mockRestore();
    }
  });

  it.each([true, false])('respects source item confirmation for a destination conflict (confirmed: %s)', confirmed => {
    component.onToLocationChange(2);
    component.lineItems.set([{
      variantId: 10,
      variantSku: 'SKU-10',
      productName: 'Product',
      availableStock: 5,
      destinationStock: 4,
      requestedQuantity: 1,
      transferQuantity: 1
    }]);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(confirmed);
    try {
      component.onToLocationChange(1);
      expect(confirm).toHaveBeenCalledOnce();
      expect(component.fromLocationId()).toBe(confirmed ? null : 1);
      expect(component.toLocationId()).toBe(confirmed ? 1 : null);
      expect(component.sameLocationSelected()).toBe(false);
      expect(component.lineItems()).toHaveLength(confirmed ? 0 : 1);
      if (!confirmed) {
        expect(component.lineItems()[0].destinationStock).toBeNull();
      }
    } finally {
      confirm.mockRestore();
    }
  });
});
