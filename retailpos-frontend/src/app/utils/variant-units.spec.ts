import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { Subject, of, throwError } from 'rxjs';
import { VariantUomConfig } from '../models/uom.model';
import { UomService } from '../services/uom.service';
import { injectVariantUnits } from './variant-units';

const config = (variantId: number, code: string): VariantUomConfig => ({
  configId: variantId * 10,
  version: 1,
  variantId,
  baseUnit: { id: 1, code, symbol: code, dimension: 'volume', precision: 2, isWhole: false },
  packStateTrackingEnabled: false,
  repackingEnabled: false,
  purchaseOptions: [],
  sellingOptions: []
});

@Component({ standalone: true, template: '' })
class HostComponent {
  readonly ids = signal<number[]>([]);
  readonly units = injectVariantUnits(() => this.ids());
}

describe('VariantUnits', () => {
  const getVariantConfigs = vi.fn();

  function create(ids: number[]) {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.ids.set(ids);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    getVariantConfigs.mockReset();
    TestBed.configureTestingModule({ providers: [{ provide: UomService, useValue: { getVariantConfigs } }] });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('leaves pcs-only rows exactly as they were', () => {
    getVariantConfigs.mockReturnValue(of([]));
    const { units } = create([1, 2]).componentInstance;

    expect(units.hasNonPcs()).toBe(false);
    expect(units.suffix(1)).toBe('');
    expect(units.pcsTotalLabel('Total Quantity')).toBe('Total Quantity');
    expect(units.pcsSuffix()).toBe('');
    expect(units.allRowsTotal('83')).toBe('83');
  });

  it('labels every row and the pcs total once another unit is on the page', () => {
    getVariantConfigs.mockReturnValue(of([config(2, 'ml')]));
    const { units } = create([1, 2]).componentInstance;

    expect(units.suffix(1)).toBe(' pcs');
    expect(units.suffix(2)).toBe(' ml');
    expect(units.pcsTotalLabel('Total Quantity')).toBe('Total (pcs)');
    expect(units.pcsSuffix()).toBe(' pcs');
  });

  it('withholds a total that adds unlike units, and labels one that does not', () => {
    getVariantConfigs.mockReturnValue(of([config(2, 'ml')]));
    expect(create([1, 2]).componentInstance.units.allRowsTotal('83')).toBe('Mixed units, see rows');

    getVariantConfigs.mockReturnValue(of([config(3, 'ml')]));
    expect(create([3]).componentInstance.units.allRowsTotal('500')).toBe('500 ml');
  });

  it('never guesses a unit when the lookup fails', () => {
    getVariantConfigs.mockReturnValue(throwError(() => new Error('403')));
    const { units } = create([1]).componentInstance;

    expect(units.unit(1)).toBeNull();
    expect(units.suffix(1)).toBe('');
    expect(units.allRowsTotal('5')).toBe('5');
  });

  it('asks once for the same rows and ignores a stale response', () => {
    const first = new Subject<VariantUomConfig[]>();
    const second = new Subject<VariantUomConfig[]>();
    getVariantConfigs.mockReturnValueOnce(first).mockReturnValueOnce(second);
    const fixture = create([1]);
    const host = fixture.componentInstance;

    host.ids.set([1]);
    fixture.detectChanges();
    expect(getVariantConfigs).toHaveBeenCalledTimes(1);

    host.ids.set([2]);
    fixture.detectChanges();
    second.next([config(2, 'kg')]);
    first.next([config(1, 'ml')]);

    expect(host.units.suffix(2)).toBe(' kg');
    expect(host.units.config(1)).toBeNull();
  });

  it('asks for nothing when there are no rows', () => {
    const { units } = create([]).componentInstance;

    expect(getVariantConfigs).not.toHaveBeenCalled();
    expect(units.hasNonPcs()).toBe(false);
  });
});
