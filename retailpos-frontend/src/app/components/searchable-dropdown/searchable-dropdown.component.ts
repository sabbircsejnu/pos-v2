import { Component, Input, Output, EventEmitter, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';

export interface DropdownOption {
  id: number;
  name: string;
  [key: string]: any;
}

@Component({
  selector: 'app-searchable-dropdown',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="relative">
      <input
        type="text"
        [(ngModel)]="searchQuery"
        (input)="onSearchInput($any($event.target).value)"
        (focus)="showDropdown.set(true)"
        [placeholder]="placeholder"
        [class]="inputClass"
        [disabled]="disabled"
      />
      
      @if (showDropdown() && !disabled) {
        <div class="absolute z-50 mt-1 w-full bg-white border border-gray-300 rounded-md shadow-lg max-h-60 overflow-hidden">
          <!-- Search Results -->
          <div 
            class="max-h-52 overflow-y-auto"
            (scroll)="onScroll($event)"
          >
            @if (isLoading()) {
              <div class="px-3 py-2 text-center text-gray-500">
                <div class="inline-block animate-spin rounded-full h-4 w-4 border-b-2 border-gray-900"></div>
                <span class="ml-2">Loading...</span>
              </div>
            } @else if (options().length === 0) {
              <div class="px-3 py-2 text-center text-gray-500">No results found</div>
            } @else {
              @for (option of options(); track option.id) {
                <div
                  (click)="selectOption(option)"
                  [class]="'px-3 py-2 hover:bg-gray-50 cursor-pointer ' + (selectedId === option.id ? 'bg-gray-100' : '')"
                >
                  <div class="text-sm font-medium text-gray-900">{{ option.name }}</div>
                  @if (showSecondaryText && option[secondaryTextField]) {
                    <div class="text-xs text-gray-500">{{ option[secondaryTextField] }}</div>
                  }
                </div>
              }
              
              <!-- Load More Indicator -->
              @if (hasMore() && !isLoadingMore()) {
                <div class="px-3 py-2 text-center text-gray-600 text-sm">
                  Scroll for more...
                </div>
              }
              
              @if (isLoadingMore()) {
                <div class="px-3 py-2 text-center text-gray-500">
                  <div class="inline-block animate-spin rounded-full h-4 w-4 border-b-2 border-gray-900"></div>
                  <span class="ml-2 text-sm">Loading more...</span>
                </div>
              }
            }
          </div>
        </div>
      }
      
      <!-- Overlay to close dropdown -->
      @if (showDropdown()) {
        <div 
          class="fixed inset-0 z-40" 
          (click)="showDropdown.set(false)"
        ></div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
    }
  `]
})
export class SearchableDropdownComponent {
  @Input() placeholder: string = 'Search...';
  @Input() inputClass: string = 'w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-gray-900';
  @Input() disabled: boolean = false;
  @Input() selectedId: number | null = null;
  @Input() showSecondaryText: boolean = false;
  @Input() secondaryTextField: string = 'description';
  
  @Output() onSelect = new EventEmitter<DropdownOption>();
  @Output() onSearch = new EventEmitter<{ query: string, page: number, pageSize: number }>();
  @Output() onLoadMore = new EventEmitter<{ query: string, page: number, pageSize: number }>();
  
  searchQuery: string = '';
  options = signal<DropdownOption[]>([]);
  showDropdown = signal<boolean>(false);
  isLoading = signal<boolean>(false);
  isLoadingMore = signal<boolean>(false);
  hasMore = signal<boolean>(true);
  
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
    this.currentPage = 1;
    this.currentQuery = '';
  }
  
  loadInitialData(): void {
    this.performSearch('', 1);
  }
}
