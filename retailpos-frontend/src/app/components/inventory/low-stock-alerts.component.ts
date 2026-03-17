import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InventoryService } from '../../services/inventory.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { LowStock } from '../../models/inventory.model';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { Outlet } from '../../models/outlet.model';
import { Warehouse } from '../../models/warehouse.model';

@Component({
  selector: 'app-low-stock-alerts',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="py-4">
      <div class="mb-4 flex justify-between items-center">
        <div>
          <h2 class="text-2xl font-semibold text-gray-900">Low Stock Alerts</h2>
          <p class="text-sm text-gray-600 mt-1">Items requiring restock</p>
        </div>
        <button (click)="refreshData()" class="btn btn-primary">
          Refresh
        </button>
      </div>

      <!-- Filters -->
      <div class="bg-white rounded-lg shadow-sm p-3 mb-4">
        <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
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
          <div class="flex items-end">
            <button type="button" (click)="loadLowStockItems()" class="btn btn-primary w-full">
              Apply Filter
            </button>
          </div>
        </div>
      </div>

      <!-- Summary Cards -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4 mb-4">
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-sm text-gray-600">Total Low Stock Items</div>
          <div class="text-2xl font-semibold text-gray-900 mt-1">{{ lowStockItems().length }}</div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-sm text-gray-600">Critical (Quantity = 0)</div>
          <div class="text-2xl font-semibold text-red-600 mt-1">
            {{ getCriticalCount() }}
          </div>
        </div>
        <div class="bg-white rounded-lg shadow-sm p-4">
          <div class="text-sm text-gray-600">Total Shortage</div>
          <div class="text-2xl font-semibold text-orange-600 mt-1">
            {{ getTotalShortage() }} units
          </div>
        </div>
      </div>

      <!-- Loading State -->
      @if (isLoading()) {
        <div class="flex justify-center items-center p-8">
          <div class="animate-spin rounded-full h-10 w-10 border-b-2 border-gray-900"></div>
        </div>
      }

      <!-- Low Stock Table -->
      @if (!isLoading() && lowStockItems().length > 0) {
        <div class="bg-white rounded-lg shadow-sm overflow-hidden">
          <table class="table">
            <thead>
              <tr>
                <th>Product</th>
                <th>Variant</th>
                <th>SKU</th>
                <th>Location</th>
                <th>Current Qty</th>
                <th>Reorder Level</th>
                <th>Shortage</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (item of lowStockItems(); track item.inventoryId) {
                <tr>
                  <td class="font-medium">{{ item.productName }}</td>
                  <td>{{ item.variantName }}</td>
                  <td class="text-gray-600">{{ item.sku || '-' }}</td>
                  <td>
                    <div class="text-sm">
                      <div class="font-medium">{{ item.locationName }}</div>
                      <div class="text-gray-500">{{ item.locationType }}</div>
                    </div>
                  </td>
                  <td>
                    <span [class]="item.currentQuantity === 0 ? 'text-red-600 font-semibold' : 'font-medium'">
                      {{ item.currentQuantity }}
                    </span>
                  </td>
                  <td>{{ item.reorderLevel }}</td>
                  <td class="font-medium text-orange-600">{{ item.shortageQuantity }}</td>
                  <td>
                    @if (item.currentQuantity === 0) {
                      <span class="badge badge-danger">Out of Stock</span>
                    } @else {
                      <span class="badge badge-warning">Low Stock</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- Empty State -->
      @if (!isLoading() && lowStockItems().length === 0) {
        <div class="bg-white rounded-lg shadow-sm p-8 text-center">
          <div class="text-4xl mb-3">✓</div>
          <p class="text-gray-600 font-medium">No low stock items</p>
          <p class="text-gray-500 text-sm mt-1">All products are adequately stocked</p>
        </div>
      }
    </div>
  `
})
export class LowStockAlertsComponent implements OnInit {
  lowStockItems = signal<LowStock[]>([]);
  outlets = signal<Outlet[]>([]);
  warehouses = signal<Warehouse[]>([]);
  isLoading = signal(false);

  filters = {
    locationType: '',
    locationId: ''
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
    this.loadLowStockItems();
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

  loadLowStockItems(): void {
    this.isLoading.set(true);

    const outletId = this.filters.locationType === 'outlet' && this.filters.locationId ? +this.filters.locationId : undefined;
    const warehouseId = this.filters.locationType === 'warehouse' && this.filters.locationId ? +this.filters.locationId : undefined;

    this.inventoryService.getLowStock(outletId, warehouseId).subscribe({
      next: (response) => {
        this.lowStockItems.set(response.data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  refreshData(): void {
    this.loadLowStockItems();
  }

  getCriticalCount(): number {
    return this.lowStockItems().filter(item => item.currentQuantity === 0).length;
  }

  getTotalShortage(): number {
    return this.lowStockItems().reduce((sum, item) => sum + item.shortageQuantity, 0);
  }
}
