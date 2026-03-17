import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';

@Component({
  selector: 'app-outlet-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './outlet-list.component.html',
  styleUrls: ['./outlet-list.component.css']
})
export class OutletListComponent implements OnInit {
  searchQuery = '';
  showDeleteConfirm = signal(false);
  outletToDelete = signal<Outlet | null>(null);

  constructor(
    public outletService: OutletService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadOutlets();
  }

  loadOutlets(): void {
    this.outletService.getAllOutlets().subscribe({
      error: (error) => {
        console.error('Error loading outlets:', error);
        this.outletService.error.set('Failed to load outlets');
      }
    });
  }

  onSearch(): void {
    if (this.searchQuery.trim()) {
      this.outletService.searchOutlets(this.searchQuery).subscribe({
        error: (error) => {
          console.error('Error searching outlets:', error);
          this.outletService.error.set('Search failed');
        }
      });
    } else {
      this.loadOutlets();
    }
  }

  createOutlet(): void {
    this.router.navigate(['/outlets/create']);
  }

  editOutlet(id: number): void {
    this.router.navigate(['/outlets/edit', id]);
  }

  viewOutlet(id: number): void {
    this.router.navigate(['/outlets', id]);
  }

  confirmDelete(outlet: Outlet): void {
    this.outletToDelete.set(outlet);
    this.showDeleteConfirm.set(true);
  }

  cancelDelete(): void {
    this.showDeleteConfirm.set(false);
    this.outletToDelete.set(null);
  }

  deleteOutlet(): void {
    const outlet = this.outletToDelete();
    if (!outlet) return;

    this.outletService.deleteOutlet(outlet.id).subscribe({
      next: () => {
        this.showDeleteConfirm.set(false);
        this.outletToDelete.set(null);
        setTimeout(() => this.outletService.clearMessages(), 3000);
      },
      error: (error) => {
        console.error('Error deleting outlet:', error);
        this.outletService.error.set(error.error?.error || 'Failed to delete outlet');
        this.showDeleteConfirm.set(false);
        setTimeout(() => this.outletService.clearMessages(), 3000);
      }
    });
  }
}
