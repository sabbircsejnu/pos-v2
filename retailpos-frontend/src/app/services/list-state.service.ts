import { Injectable } from '@angular/core';
import { ActivatedRoute, Params, Router } from '@angular/router';

/**
 * ListStateService
 *
 * Provides a reusable mechanism for list pages to persist their filter,
 * pagination, sorting, and view-mode state in the URL as query parameters.
 *
 * Benefits:
 * - State survives browser refresh (URL is preserved).
 * - Browser Back button naturally restores state without any extra code.
 * - URLs are deep-linkable and shareable.
 *
 * Usage pattern (in a list component):
 *
 *   // On init — restore from URL
 *   ngOnInit() {
 *     const p = this.route.snapshot.queryParams;
 *     this.searchQuery.set(this.listState.str(p, 'search'));
 *     this.pageNumber.set(this.listState.num(p, 'page', 1));
 *     this.loadData();
 *   }
 *
 *   // On any state change — write full current state to URL
 *   private syncUrl(): void {
 *     this.listState.update(this.route, {
 *       search: this.searchQuery() || undefined,
 *       page:   this.pageNumber(),
 *     });
 *   }
 *
 *   // On "Clear Filters"
 *   clearFilters() {
 *     this.listState.clear(this.route);
 *     // reset signals to defaults, then reload
 *   }
 *
 * See: Docs/01_Architecture/LIST_STATE_PRESERVATION.md
 */
@Injectable({ providedIn: 'root' })
export class ListStateService {
  constructor(private router: Router) {}

  /**
   * Writes the supplied state record to the current route's URL as query parameters.
   * Entries with undefined, null, or empty-string values are omitted to keep URLs clean.
   * Uses replaceUrl:true so incremental filter/page changes do not push new history entries —
   * the browser Back button returns to the page the user was on before the list, not to
   * every individual filter change.
   */
  update(
    route: ActivatedRoute,
    params: Record<string, string | number | boolean | null | undefined>,
  ): void {
    const clean: Record<string, string | number | boolean> = {};
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') {
        clean[k] = v as string | number | boolean;
      }
    }
    this.router.navigate([], {
      relativeTo: route,
      queryParams: clean,
      replaceUrl: true,
    });
  }

  /**
   * Removes all query parameters from the current route URL.
   * Call this from "Clear Filters" handlers.
   */
  clear(route: ActivatedRoute): void {
    this.router.navigate([], {
      relativeTo: route,
      queryParams: {},
      replaceUrl: true,
    });
  }

  /** Read a string param. Returns fallback (default '') if the param is absent. */
  str(params: Params, key: string, fallback = ''): string {
    return params[key] != null ? String(params[key]) : fallback;
  }

  /**
   * Read a numeric param. Returns fallback if the param is absent or not a valid number.
   */
  num(params: Params, key: string, fallback: number): number {
    const v = Number(params[key]);
    return params[key] != null && !isNaN(v) ? v : fallback;
  }

  /**
   * Read a boolean param that can also be absent.
   * Returns true for the string 'true', false for 'false', undefined if absent.
   * Useful for three-state filters (e.g., isActive: true | false | undefined = all).
   */
  boolOrUndef(params: Params, key: string): boolean | undefined {
    if (params[key] === 'true') return true;
    if (params[key] === 'false') return false;
    return undefined;
  }

  /**
   * Read a numeric ID param that is optional.
   * Returns undefined if the param is absent, zero, or not a positive integer.
   */
  optionalId(params: Params, key: string): number | undefined {
    const v = Number(params[key]);
    return params[key] != null && !isNaN(v) && v > 0 ? v : undefined;
  }

  /**
   * Read a numeric param that may be zero.
   * Returns undefined if the param is absent or not a valid non-negative integer.
   * Use this for filters where 0 is a meaningful sentinel value
   * (e.g., outletId=0 means "users with no outlet assigned").
   */
  numOrZero(params: Params, key: string): number | undefined {
    if (params[key] == null) return undefined;
    const v = Number(params[key]);
    return !isNaN(v) && v >= 0 ? v : undefined;
  }
}
