import { DestroyRef, Injector, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { VariantUomConfig } from '../models/uom.model';
import { UomService } from '../services/uom.service';
import { PCS_UNIT_CODE } from './uom-quantity.util';

/**
 * The base units of the rows a page is showing. A variant missing from the result is plain pcs. If the lookup fails,
 * rows keep their bare numbers: an unknown unit is never guessed as pcs, and the page still works.
 */
export class VariantUnits {
  private readonly configs = signal<ReadonlyMap<number, VariantUomConfig>>(new Map());
  private readonly known = signal<ReadonlySet<number>>(new Set());
  private request: Subscription | null = null;

  /** True when a row on this page is measured in something other than pcs. */
  readonly hasNonPcs = computed(() => [...this.configs().values()].some(config => config.baseUnit.code !== PCS_UNIT_CODE));

  /** More than one unit on the page, so one total cannot describe it. */
  readonly distinctUnits = computed(() => {
    const units = new Set<string>();
    for (const id of this.known()) {
      units.add(this.configs().get(id)?.baseUnit.code ?? PCS_UNIT_CODE);
    }
    return [...units];
  });

  constructor(private readonly injector: Injector) {}

  load(variantIds: readonly number[]): void {
    this.request?.unsubscribe();
    const ids = [...new Set(variantIds)].filter(id => id > 0);
    if (ids.length === 0) {
      this.apply([], []);
      return;
    }
    this.request = this.injector.get(UomService).getVariantConfigs(ids).subscribe({
      next: configs => this.apply(ids, configs),
      error: () => this.apply([], [])
    });
  }

  dispose(): void {
    this.request?.unsubscribe();
  }

  config(variantId: number): VariantUomConfig | null {
    return this.configs().get(variantId) ?? null;
  }

  /** Null until the variant's unit is known. */
  unit(variantId: number): string | null {
    if (!this.known().has(variantId)) {
      return null;
    }
    return this.configs().get(variantId)?.baseUnit.code ?? PCS_UNIT_CODE;
  }

  /** Text to place after a quantity. Pcs stay unlabelled, as they always were, unless other units share the page. */
  suffix(variantId: number): string {
    const unit = this.unit(variantId);
    if (!unit || (unit === PCS_UNIT_CODE && !this.hasNonPcs())) {
      return '';
    }
    return ` ${unit}`;
  }

  /** For a server total that covers pcs only. Keeps today's wording until other units are on the page. */
  pcsTotalLabel(todayLabel: string, pcsLabel = 'Total (pcs)'): string {
    return this.hasNonPcs() ? pcsLabel : todayLabel;
  }

  /** Placed after a server total that covers pcs only, once other units are on the page. */
  pcsSuffix(): string {
    return this.hasNonPcs() ? ' pcs' : '';
  }

  /**
   * For a server total that still adds every row. One unit on the page gives that unit; several have no single figure,
   * so the total is withheld instead of showing `pcs + ml` as a number.
   */
  allRowsTotal(formatted: string | null): string {
    const units = this.distinctUnits();
    if (units.length > 1) {
      return 'Mixed units, see rows';
    }
    return units.length === 1 && units[0] !== PCS_UNIT_CODE ? `${formatted} ${units[0]}` : formatted ?? '';
  }

  private apply(ids: readonly number[], configs: readonly VariantUomConfig[]): void {
    this.known.set(new Set(ids));
    this.configs.set(new Map(configs.map(config => [config.variantId, config])));
  }
}

/** Must run in an injection context. With `variantIds`, follows the visible rows and ignores stale responses. */
export function injectVariantUnits(variantIds?: () => readonly number[]): VariantUnits {
  const units = new VariantUnits(inject(Injector));
  inject(DestroyRef).onDestroy(() => units.dispose());
  if (variantIds) {
    const key = computed(() => [...new Set(variantIds())].sort((a, b) => a - b).join(','));
    effect(() => {
      const ids = key();
      untracked(() => units.load(ids ? ids.split(',').map(Number) : []));
    });
  }
  return units;
}
