import { Component, ElementRef, EventEmitter, Input, Output, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';

export interface DropdownOption {
  id: number;
  name: string;
  [key: string]: any;
}

let nextDropdownId = 0;

/**
 * Server-driven searchable select. The parent owns the data: it handles `onSearch` / `onLoadMore`
 * and calls `setOptions`, `setHasMore` and `setLoading`. Presentation uses the shared design tokens;
 * the control is a combobox with a listbox (Arrow keys, Enter, Escape, Tab) and an optional clear
 * button (`clearable`, `cleared`). Inputs, outputs and public methods are unchanged.
 */
@Component({
  selector: 'app-searchable-dropdown',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="sd">
      <div class="sd__field">
        <input
          type="text"
          role="combobox"
          aria-autocomplete="list"
          autocomplete="off"
          [attr.aria-expanded]="showDropdown() && !disabled"
          [attr.aria-controls]="listId"
          [attr.aria-activedescendant]="activeOptionId()"
          [attr.aria-label]="ariaLabel || placeholder"
          [(ngModel)]="searchQuery"
          (input)="onSearchInput($any($event.target).value)"
          (focus)="showDropdown.set(true)"
          (keydown)="onKeydown($event)"
          [placeholder]="placeholder"
          [class]="inputClass"
          [class.sd__input--clearable]="clearable"
          [disabled]="disabled"
        />
        @if (clearable && !disabled && (searchQuery || selectedId !== null)) {
          <button type="button" class="sd__clear" aria-label="Clear selection" (click)="onClear()">
            <i class="fas fa-times" aria-hidden="true"></i>
          </button>
        }
      </div>

      @if (showDropdown() && !disabled) {
        <div class="sd__panel">
          <div class="sd__list" [id]="listId" role="listbox" [attr.aria-label]="ariaLabel || placeholder" (scroll)="onScroll($event)">
            @if (isLoading()) {
              <div class="sd__status" role="status">
                <span class="sd__spinner" aria-hidden="true"></span>
                <span>Loading...</span>
              </div>
            } @else if (options().length === 0) {
              <div class="sd__status" role="status">No results found</div>
            } @else {
              @for (option of options(); track option.id; let i = $index) {
                <div
                  role="option"
                  class="sd__option"
                  [id]="optionId(i)"
                  [attr.aria-selected]="selectedId === option.id"
                  [class.sd__option--active]="i === activeIndex()"
                  [class.sd__option--selected]="selectedId === option.id"
                  (click)="selectOption(option)"
                  (mouseenter)="activeIndex.set(i)"
                >
                  <div class="sd__option-name">{{ option.name }}</div>
                  @if (showSecondaryText && option[secondaryTextField]) {
                    <div class="sd__option-secondary">{{ option[secondaryTextField] }}</div>
                  }
                </div>
              }

              @if (hasMore() && !isLoadingMore()) {
                <div class="sd__status sd__status--small">Scroll for more...</div>
              }

              @if (isLoadingMore()) {
                <div class="sd__status sd__status--small" role="status">
                  <span class="sd__spinner" aria-hidden="true"></span>
                  <span>Loading more...</span>
                </div>
              }
            }
          </div>
        </div>
      }

      <!-- Overlay to close dropdown -->
      @if (showDropdown()) {
        <div class="sd__overlay" aria-hidden="true" (click)="showDropdown.set(false)"></div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
    }

    .sd {
      position: relative;
      font-family: var(--rp-font-family-base);
    }

    .sd__field {
      position: relative;
    }

    .sd__input--clearable {
      padding-right: 2.25rem;
    }

    .sd__clear {
      position: absolute;
      top: 50%;
      right: 0.375rem;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 1.75rem;
      height: 1.75rem;
      border-radius: var(--rp-radius-control);
      color: var(--rp-color-text-secondary);
      transform: translateY(-50%);
    }

    .sd__clear:hover {
      background: var(--rp-color-surface-hover);
      color: var(--rp-color-text-primary);
    }

    .sd__clear:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: 1px;
    }

    .sd__panel {
      position: absolute;
      top: calc(100% + 4px);
      left: 0;
      z-index: 50;
      width: 100%;
      overflow: hidden;
      border: 1px solid var(--rp-color-border-control);
      border-radius: var(--rp-radius-control);
      background: var(--rp-color-surface-card);
      box-shadow: var(--rp-shadow-popover);
    }

    .sd__list {
      max-height: 13rem;
      overflow-y: auto;
    }

    .sd__option {
      padding: var(--rp-space-2) var(--rp-space-3);
      cursor: pointer;
    }

    .sd__option--active {
      background: var(--rp-color-surface-hover);
    }

    .sd__option--selected {
      background: var(--rp-color-primary-soft);
      box-shadow: inset 3px 0 0 var(--rp-color-primary);
    }

    .sd__option-name {
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-body-size);
      font-weight: var(--rp-font-weight-medium);
    }

    .sd__option-secondary {
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
    }

    .sd__status {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--rp-space-2);
      padding: var(--rp-space-2) var(--rp-space-3);
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-body-size);
      text-align: center;
    }

    .sd__status--small {
      font-size: var(--rp-text-label-size);
    }

    .sd__spinner {
      width: 1rem;
      height: 1rem;
      border: 2px solid var(--rp-color-primary);
      border-right-color: transparent;
      border-radius: var(--rp-radius-pill);
      animation: sd-spin 800ms linear infinite;
    }

    .sd__overlay {
      position: fixed;
      inset: 0;
      z-index: 40;
    }

    @keyframes sd-spin {
      to {
        transform: rotate(360deg);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .sd__spinner {
        animation-duration: 2s;
      }
    }
  `]
})
export class SearchableDropdownComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input() placeholder: string = 'Search...';
  @Input() inputClass: string = 'ui-control';
  @Input() disabled: boolean = false;
  @Input() selectedId: number | null = null;
  @Input() showSecondaryText: boolean = false;
  @Input() secondaryTextField: string = 'description';
  /** Accessible name for the combobox; defaults to the placeholder. */
  @Input() ariaLabel: string = '';
  /** Shows a clear button while there is a selection or text. */
  @Input() clearable: boolean = false;

  @Output() onSelect = new EventEmitter<DropdownOption>();
  @Output() onSearch = new EventEmitter<{ query: string, page: number, pageSize: number }>();
  @Output() onLoadMore = new EventEmitter<{ query: string, page: number, pageSize: number }>();
  /** Emitted after the clear button reset the control (the parent should drop its own selection). */
  @Output() cleared = new EventEmitter<void>();

  searchQuery: string = '';
  options = signal<DropdownOption[]>([]);
  showDropdown = signal<boolean>(false);
  isLoading = signal<boolean>(false);
  isLoadingMore = signal<boolean>(false);
  hasMore = signal<boolean>(true);
  activeIndex = signal<number>(-1);

  private readonly instanceId = ++nextDropdownId;
  readonly listId = `sd-list-${this.instanceId}`;

  private searchSubject = new Subject<string>();
  private currentPage = 1;
  private pageSize = 20;
  private currentQuery = '';

  constructor() {
    // Debounce search input
    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe(query => {
      this.performSearch(query, 1);
    });

    // Watch for selected ID changes to update display
    effect(() => {
      const id = this.selectedId;
      const selected = this.options().find(o => o.id === id);
      if (selected) {
        this.searchQuery = selected.name;
      }
    });
  }

  optionId(index: number): string {
    return `${this.listId}-option-${index}`;
  }

  activeOptionId(): string | null {
    const index = this.activeIndex();
    return this.showDropdown() && index >= 0 && index < this.options().length ? this.optionId(index) : null;
  }

  onKeydown(event: KeyboardEvent): void {
    const count = this.options().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.showDropdown()) {
          this.showDropdown.set(true);
        }
        if (count > 0) {
          this.moveActive(Math.min(count - 1, this.activeIndex() + 1));
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (count > 0) {
          this.moveActive(Math.max(0, this.activeIndex() - 1));
        }
        break;
      case 'Enter': {
        const option = this.options()[this.activeIndex()];
        if (this.showDropdown() && option) {
          event.preventDefault();
          this.selectOption(option);
        }
        break;
      }
      case 'Escape':
        if (this.showDropdown()) {
          // Do not let Escape also close a dialog or drawer that hosts this control.
          event.preventDefault();
          event.stopPropagation();
          this.showDropdown.set(false);
        }
        break;
      case 'Tab':
        this.showDropdown.set(false);
        break;
    }
  }

  onSearchInput(value: string): void {
    this.searchSubject.next(value);
  }

  performSearch(query: string, page: number): void {
    this.currentQuery = query;
    this.currentPage = page;

    if (page === 1) {
      this.isLoading.set(true);
      this.options.set([]);
    }

    this.onSearch.emit({ query, page, pageSize: this.pageSize });
  }

  selectOption(option: DropdownOption): void {
    this.selectedId = option.id;
    this.searchQuery = option.name;
    this.showDropdown.set(false);
    this.onSelect.emit(option);
  }

  onClear(): void {
    this.clear();
    this.cleared.emit();
    this.host.nativeElement.querySelector('input')?.focus();
  }

  onScroll(event: any): void {
    const element = event.target;
    const threshold = 50; // pixels from bottom

    if (element.scrollHeight - element.scrollTop - element.clientHeight < threshold) {
      if (this.hasMore() && !this.isLoadingMore() && !this.isLoading()) {
        this.loadMore();
      }
    }
  }

  loadMore(): void {
    this.isLoadingMore.set(true);
    this.currentPage++;
    this.onLoadMore.emit({
      query: this.currentQuery,
      page: this.currentPage,
      pageSize: this.pageSize
    });
  }

  // Public methods for parent component to call
  setOptions(options: DropdownOption[], append: boolean = false): void {
    if (append) {
      this.options.update(current => [...current, ...options]);
    } else {
      this.options.set(options);
      this.activeIndex.set(-1);
    }
    this.isLoading.set(false);
    this.isLoadingMore.set(false);
  }

  setHasMore(hasMore: boolean): void {
    this.hasMore.set(hasMore);
  }

  setLoading(loading: boolean): void {
    this.isLoading.set(loading);
  }

  clear(): void {
    this.searchQuery = '';
    this.selectedId = null;
    this.options.set([]);
    this.activeIndex.set(-1);
    this.currentPage = 1;
    this.currentQuery = '';
  }

  loadInitialData(): void {
    this.performSearch('', 1);
  }

  private moveActive(index: number): void {
    this.activeIndex.set(index);
    // Keep the active option visible while arrowing through a long, scrollable list.
    queueMicrotask(() => {
      this.host.nativeElement.querySelector<HTMLElement>(`#${this.optionId(index)}`)?.scrollIntoView?.({ block: 'nearest' });
    });
  }
}
