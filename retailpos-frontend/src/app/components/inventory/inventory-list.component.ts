import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InventoryService } from '../../services/inventory.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { Inventory, InventorySearchRequest } from '../../models/inventory.model';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { Outlet } from '../../models/outlet.model';
import { Warehouse } from '../../models/warehouse.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';

@Component({
  selector: 'app-inventory-list',
  standalone: true,
  imports: [CommonModule, FormsModule, AppCurrencyPipe],
  template: `
    <div class="py-4">
      <div class="mb-4 flex justify-between items-center">
        <h2 class="text-2xl font-semibold text-gray-900">Inventory Management</h2>
      </div>

      <!-- Filters -->
      <div class="bg-white rounded-lg shadow-sm p-3 mb-4">
        <div class="grid grid-cols-1 md:grid-cols-4 gap-3">
          <div>
            <label class="label">Product Name</label>
            <input
              type="text"
              class="input"
              [(ngModel)]="filters.productName"
              placeholder="Search by product name"
            />
          </div>
          <div>
            <label class="label">SKU</label>
            <input
              type="text"
              class="input"
              [(ngModel)]="filters.sku"
              placeholder="Search by SKU"
            />
          </div>
          <div>
            <label class="label">Location Type</label>
            <select class="select" [(ngModel)]="filters.locationType" (change)="onLocationTypeChange()">
              <option value="">All Locations</option>
              <option value="outlet">Outlets</option>
              <option value="warehouse">Warehouses</option>
            </select>
          </div>
          <div>
            <label class="label">Specific Location</label>
            <select class="select" [(ngModel)]="filters.locationId" [disabled]="!filters.locationType">
              <option value="">All</option>
              @if (filters.locationType === 'outlet') {
                @for (outlet of outlets(); track outlet.id) {
                  <option [value]="outlet.id">{{ outlet.name }}</option>
                }
              }
              @if (filters.locationType === 'warehouse') {
                @for (warehouse of warehouses(); track warehouse.id) {
                  <option [value]="warehouse.id">{{ warehouse.name }}</option>
                }
              }
            </select>
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-4 gap-3 mt-3">
          <div class="flex items-center">
            <input
              type="checkbox"
              id="lowStockOnly"
              [(ngModel)]="filters.lowStockOnly"
              class="mr-2"
            />
            <label for="lowStockOnly" class="text-sm text-gray-700">Low Stock Only</label>
          </div>
          <div class="flex items-center">
            <input
              type="checkbox"
              id="outOfStockOnly"
              [(ngModel)]="filters.outOfStockOnly"
              class="mr-2"
            />
            <label for="outOfStockOnly" class="text-sm text-gray-700">Out of Stock Only</label>
          </div>
          <div class="flex items-center">
            <input
              type="checkbox"
              id="expiringSoon"
              [(ngModel)]="filters.expiringSoon"
              class="mr-2"
            />
            <label for="expiringSoon" class="text-sm text-gray-700">Expiring Soon</label>
          </div>
          <div class="flex gap-2">
            <button type="button" (click)="searchInventory()" class="btn btn-primary">
              Search
            </button>
            <button type="button" (click)="clearFilters()" class="btn btn-outline">
              Clear
            </button>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      @if (isLoading()) {
        <div class="flex justify-center items-center p-8">
          <div class="animate-spin rounded-full h-10 w-10 border-b-2 border-gray-900"></div>
        </div>
      }

      <!-- Inventory Table -->
      @if (!isLoading() && inventories().length > 0) {
        <div class="bg-white rounded-lg shadow-sm overflow-hidden">
          <table class="table">
            <thead>
              <tr>
                <th>Product</th>
                <th>Variant</th>
                <th>SKU</th>
                <th>Location</th>
                <th>Quantity</th>
                <th>Reorder Level</th>
                <th>Status</th>
                <th>Cost Price</th>
                <th>Retail Price</th>
                <th>Total Value</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (item of inventories(); track item.id) {
                <tr>
                  <td class="font-medium">{{ item.productName }}</td>
                  <td>{{ item.variantName }}</td>
                  <td class="text-gray-600">{{ item.sku || '-' }}</td>
                  <td>
                    <div class="text-sm">
                      <div class="font-medium">{{ item.outletName || item.warehouseName }}</div>
                      <div class="text-gray-500">{{ item.locationType }}</div>
                    </div>
                  </td>
                  <td class="font-medium">{{ item.quantity }}</td>
                  <td>{{ item.reorderLevel }}</td>
                  <td>
                    @if (item.isOutOfStock) {
                      <span class="badge badge-danger">Out of Stock</span>
                    } @else if (item.isLowStock) {
                      <span class="badge badge-warning">Low Stock</span>
                    } @else if (item.isExpiringSoon) {
                      <span class="badge badge-warning">Expiring Soon</span>
                    } @else {
                      <span class="badge badge-success">In Stock</span>
                    }
                  </td>
                  <td>{{ item.costPrice | appCurrency }}</td>
                  <td>{{ item.retailPrice | appCurrency }}</td>
                  <td class="font-medium">{{ item.totalValue | appCurrency }}</td>
                  <td>
                    <button
                      type="button"
                      (click)="openThresholdModal(item)"
                      class="text-gray-600 hover:text-gray-900 text-sm"
                    >
                      Adjust Threshold
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Pagination -->
        <div class="mt-4 flex justify-between items-center">
          <div class="text-sm text-gray-600">
            Showing {{ inventories().length }} items
          </div>
          <div class="flex gap-1">
            <button
              (click)="changePage(currentPage() - 1)"
              [disabled]="currentPage() === 1"
              class="px-3 py-1.5 text-sm border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Previous
            </button>
            <button
              (click)="changePage(currentPage() + 1)"
              [disabled]="inventories().length < pageSize()"
              class="px-3 py-1.5 text-sm border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Next
            </button>
          </div>
        </div>
      }

      <!-- Empty State -->
      @if (!isLoading() && inventories().length === 0) {
        <div class="bg-white rounded-lg shadow-sm p-8 text-center">
          <p class="text-gray-500">No inventory items found</p>
        </div>
      }
    </div>

    <!-- Threshold Modal -->
    @if (showThresholdModal()) {
      <div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
        <div class="bg-white rounded-lg shadow-xl w-full max-w-md">
          <div class="p-4 border-b">
            <h3 class="text-lg font-semibold">Update Stock Threshold</h3>
            <p class="text-sm text-gray-600 mt-1">
              {{ selectedItem()?.productName }} - {{ selectedItem()?.variantName }}
            </p>
          </div>
          <div class="p-4">
            <div class="mb-4">
              <label class="label">Reorder Level</label>
              <input
                type="number"
                class="input"
                [(ngModel)]="thresholdData.reorderLevel"
                min="0"
              />
            </div>
            <div class="mb-4">
              <label class="label">Max Stock Level (Optional)</label>
              <input
                type="number"
                class="input"
                [(ngModel)]="thresholdData.maxStockLevel"
                min="0"
              />
            </div>
          </div>
          <div class="p-4 border-t flex justify-end gap-2">
            <button type="button" (click)="closeThresholdModal()" class="btn btn-outline">
              Cancel
            </button>
            <button type="button" (click)="saveThreshold()" class="btn btn-primary">
              Save
            </button>
          </div>
        </div>
      </div>
    }
  `
})
export class InventoryListComponent implements OnInit {
  inventories = signal<Inventory[]>([]);
  outlets = signal<Outlet[]>([]);
  warehouses = signal<Warehouse[]>([]);
  isLoading = signal(false);
  currentPage = signal(1);
  pageSize = signal(25);
  
  showThresholdModal = signal(false);
  selectedItem = signal<Inventory | null>(null);
  thresholdData = { reorderLevel: 0, maxStockLevel: undefined as number | undefined };

  filters = {
    productName: '',
    sku: '',
    locationType: '',
    locationId: '',
    lowStockOnly: false,
    outOfStockOnly: false,
    expiringSoon: false
  };

  constructor(
    private inventoryService: InventoryService,
    private outletService: OutletService,
    private warehouseService: WarehouseService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.loadOutlets();
    this.loadWarehouses();
    this.searchInventory();
  }

  loadOutlets(): void {
    this.outletService.getAllOutlets().subscribe({
      next: (response: any) => this.outlets.set(response.data),
      error: (err: any) => console.error('Failed to load outlets', err)
    });
  }

  loadWarehouses(): void {
    this.warehouseService.getAllWarehouses().subscribe({
      next: (response: any) => this.warehouses.set(response.data),
      error: (err: any) => console.error('Failed to load warehouses', err)
    });
  }

  onLocationTypeChange(): void {
    this.filters.locationId = '';
  }

  searchInventory(): void {
    this.isLoading.set(true);

    const request: InventorySearchRequest = {
      productName: this.filters.productName || undefined,
      sku: this.filters.sku || undefined,
      outletId: this.filters.locationType === 'outlet' && this.filters.locationId ? +this.filters.locationId : undefined,
      warehouseId: this.filters.locationType === 'warehouse' && this.filters.locationId ? +this.filters.locationId : undefined,
      lowStockOnly: this.filters.lowStockOnly || undefined,
      outOfStockOnly: this.filters.outOfStockOnly || undefined,
      expiringSoon: this.filters.expiringSoon || undefined,
      expiringWithinDays: this.filters.expiringSoon ? 30 : undefined,
      page: this.currentPage(),
      pageSize: this.pageSize()
    };

    this.inventoryService.search(request).subscribe({
      next: (response) => {
        this.inventories.set(response.data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  clearFilters(): void {
    this.filters = {
      productName: '',
      sku: '',
      locationType: '',
      locationId: '',
      lowStockOnly: false,
      outOfStockOnly: false,
      expiringSoon: false
    };
    this.currentPage.set(1);
    this.searchInventory();
  }

  changePage(page: number): void {
    if (page < 1) return;
    this.currentPage.set(page);
    this.searchInventory();
  }

  openThresholdModal(item: Inventory): void {
    this.selectedItem.set(item);
    this.thresholdData = {
      reorderLevel: item.reorderLevel,
      maxStockLevel: item.maxStockLevel || undefined
    };
    this.showThresholdModal.set(true);
  }

  closeThresholdModal(): void {
    this.showThresholdModal.set(false);
    this.selectedItem.set(null);
  }

  saveThreshold(): void {
    const item = this.selectedItem();
    if (!item) return;

    this.inventoryService.updateStockThreshold(item.id, this.thresholdData).subscribe({
      next: () => {
        this.alertService.success('Stock threshold updated successfully');
        this.closeThresholdModal();
        this.searchInventory();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }
}
