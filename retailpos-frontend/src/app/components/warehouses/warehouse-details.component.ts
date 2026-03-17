import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { WarehouseService } from '../../services/warehouse.service';
import { Warehouse } from '../../models/warehouse.model';

@Component({
  selector: 'app-warehouse-details',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './warehouse-details.component.html',
  styleUrls: ['./warehouse-details.component.css']
})
export class WarehouseDetailsComponent implements OnInit {
  warehouse = signal<Warehouse | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);

  constructor(
    private warehouseService: WarehouseService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadWarehouse(+id);
    }
  }

  loadWarehouse(id: number): void {
    this.isLoading.set(true);
    this.warehouseService.getWarehouseById(id).subscribe({
      next: (response: any) => {
        this.warehouse.set(response.data);
        this.isLoading.set(false);
      },
      error: (error: any) => {
        console.error('Error loading warehouse:', error);
        this.error.set('Failed to load warehouse details');
        this.isLoading.set(false);
      }
    });
  }

  editWarehouse(): void {
    const warehouse = this.warehouse();
    if (warehouse) {
      this.router.navigate(['/warehouses/edit', warehouse.id]);
    }
  }

  backToList(): void {
    this.router.navigate(['/warehouses']);
  }
}
