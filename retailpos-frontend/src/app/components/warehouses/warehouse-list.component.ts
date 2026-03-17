import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { WarehouseService } from '../../services/warehouse.service';
import { Warehouse } from '../../models/warehouse.model';

@Component({
  selector: 'app-warehouse-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './warehouse-list.component.html',
  styleUrls: ['./warehouse-list.component.css']
})
export class WarehouseListComponent implements OnInit {
  searchQuery = '';
  showDeleteConfirm = signal(false);
  warehouseToDelete = signal<Warehouse | null>(null);

  constructor(
    public warehouseService: WarehouseService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadWarehouses();
  }

  loadWarehouses(): void {
    this.warehouseService.getAllWarehouses().subscribe({
      error: (error) => {
        console.error('Error loading warehouses:', error);
        this.warehouseService.error.set('Failed to load warehouses');
      }
    });
  }

  onSearch(): void {
    if (this.searchQuery.trim()) {
      this.warehouseService.searchWarehouses(this.searchQuery).subscribe({
        error: (error) => {
          console.error('Error searching warehouses:', error);
          this.warehouseService.error.set('Search failed');
        }
      });
    } else {
      this.loadWarehouses();
    }
  }

  createWarehouse(): void {
    this.router.navigate(['/warehouses/create']);
  }

  editWarehouse(id: number): void {
    this.router.navigate(['/warehouses/edit', id]);
  }

  viewWarehouse(id: number): void {
    this.router.navigate(['/warehouses', id]);
  }

  confirmDelete(warehouse: Warehouse): void {
    this.warehouseToDelete.set(warehouse);
    this.showDeleteConfirm.set(true);
  }

  cancelDelete(): void {
    this.showDeleteConfirm.set(false);
    this.warehouseToDelete.set(null);
  }

  deleteWarehouse(): void {
    const warehouse = this.warehouseToDelete();
    if (!warehouse) return;

    this.warehouseService.deleteWarehouse(warehouse.id).subscribe({
      next: () => {
        this.showDeleteConfirm.set(false);
        this.warehouseToDelete.set(null);
        setTimeout(() => this.warehouseService.clearMessages(), 3000);
      },
      error: (error) => {
        console.error('Error deleting warehouse:', error);
        this.warehouseService.error.set(error.error?.error || 'Failed to delete warehouse');
        this.showDeleteConfirm.set(false);
        setTimeout(() => this.warehouseService.clearMessages(), 3000);
      }
    });
  }
}
