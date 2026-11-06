import { Location } from '@angular/common';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
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

describe('TransferCreateComponent recommendation prefill', () => {
  const outlets = [{ id: 1, name: 'Outlet A' }, { id: 2, name: 'Outlet B' }];
  const prefill = {
    fromLocationId: '1',
    fromLocationType: 'outlet',
    toLocationId: '2',
    toLocationType: 'outlet',
    variantId: '11',
    sku: 'SKU-11',
    quantity: '5'
  };
  const searchVariants = vi.fn();
  const warning = vi.fn();
  const create = vi.fn();

  async function setup(query: Record<string, string>, canSelectSource = true, defaultId: number | null = 2) {
    searchVariants.mockReset();
    warning.mockReset();
    create.mockReset();
    searchVariants.mockReturnValue(of({
      data: [{ id: 11, sku: 'SKU-11', productName: 'Borka', productCode: 'MAIN-1', stockQuantity: 3 }]
    }));
    await TestBed.configureTestingModule({
      imports: [TransferCreateComponent],
      providers: [
        { provide: StockTransferService, useValue: { create } },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: {
          outlets,
          warehouses: [],
          destinationOutlets: [...outlets, { id: 3, name: 'Outlet C' }],
          destinationWarehouses: [],
          defaultLocationType: 'outlet',
          defaultLocationId: defaultId
        } }) } },
        { provide: AuthService, useValue: { hasPermission: () => canSelectSource } },
        { provide: Router, useValue: {} },
        { provide: Location, useValue: {} },
        { provide: AlertService, useValue: { warning, error: vi.fn(), success: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => '' } },
        { provide: ProductImageService, useValue: {} },
        { provide: ProductService, useValue: { searchVariants } },
        { provide: UnsavedChangesService, useValue: {} },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }
      ]
    })
      .overrideComponent(TransferCreateComponent, {
        remove: { imports: [VariantSearchPickerComponent] },
        add: { schemas: [NO_ERRORS_SCHEMA] }
      })
      .compileComponents();
    const component = TestBed.createComponent(TransferCreateComponent).componentInstance;
    component.ngOnInit();
    return component;
  }

  it('fills locations and the line from live stock, and submits nothing', async () => {
    const component = await setup(prefill);

    expect(component.fromLocationId()).toBe(1);
    expect(component.toLocationId()).toBe(2);
    expect(searchVariants).toHaveBeenCalledWith('SKU-11', 1, 20, 1, 'outlet');
    expect(component.lineItems()[0]).toMatchObject({ variantId: 11, availableStock: 3, transferQuantity: 5 });
    expect(create).not.toHaveBeenCalled();
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('flags a stale quantity above live stock through the normal validation', async () => {
    const component = await setup(prefill);

    expect(component.getTransferQuantityError(component.lineItems()[0])).toContain('cannot exceed available stock');
    expect(component.canSubmit()).toBe(false);
  });

  it('refuses a source outside the transfer source locations', async () => {
    const component = await setup({ ...prefill, fromLocationId: '3' });

    expect(warning).toHaveBeenCalled();
    expect(component.lineItems()).toEqual([]);
    expect(searchVariants).not.toHaveBeenCalled();
  });

  it('refuses another source for a user pinned to their default', async () => {
    const component = await setup(prefill, false, 2);

    expect(warning).toHaveBeenCalled();
    expect(component.lineItems()).toEqual([]);
  });

  it('refuses identical locations and malformed values', async () => {
    await setup({ ...prefill, toLocationId: '1' });
    expect(warning).toHaveBeenCalled();
    TestBed.resetTestingModule();

    await setup({ ...prefill, quantity: '-4' });
    expect(warning).toHaveBeenCalled();
    expect(searchVariants).not.toHaveBeenCalled();
  });

  it('leaves the form untouched without a recommendation', async () => {
    const component = await setup({});

    expect(searchVariants).not.toHaveBeenCalled();
    expect(warning).not.toHaveBeenCalled();
    expect(component.lineItems()).toEqual([]);
  });
});
