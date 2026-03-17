import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { GrnService } from '../../services/grn.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { Grn, GrnVariance } from '../../models/grn.model';

@Component({
  selector: 'app-grn-details',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './grn-details.component.html',
  styleUrls: ['./grn-details.component.css']
})
export class GrnDetailsComponent implements OnInit {
  grn = signal<Grn | null>(null);
  variance = signal<GrnVariance | null>(null);
  isLoading = signal(false);
  isCompleting = signal(false);

  constructor(
    private grnService: GrnService,
    private router: Router,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadGrn(+id);
      this.loadVariance(+id);
    }
  }

  loadGrn(id: number): void {
    this.isLoading.set(true);
    this.grnService.getById(id).subscribe({
      next: (response) => {
        this.grn.set(response.data || null);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
        this.router.navigate(['/grn']);
      }
    });
  }

  loadVariance(id: number): void {
    this.grnService.getVariance(id).subscribe({
      next: (response) => this.variance.set(response.data || null),
      error: () => { /* variance is optional */ }
    });
  }

  completeGrn(): void {
    const grn = this.grn();
    if (!grn) return;
    this.alertService.confirm('Complete this GRN and update stock levels?', () => {
      this.isCompleting.set(true);
      this.grnService.complete(grn.id).subscribe({
        next: () => {
          this.alertService.success('GRN completed and stock updated successfully');
          this.loadGrn(grn.id);
          this.isCompleting.set(false);
        },
        error: (err) => {
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
          this.isCompleting.set(false);
        }
      });
    });
  }

  backToList(): void {
    this.router.navigate(['/grn']);
  }

  getStatusBadgeClass(status: string): string {
    switch (status?.toLowerCase()) {
      case 'full': return 'bg-green-100 text-green-800';
      case 'partial': return 'bg-yellow-100 text-yellow-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  }

  getVarianceClass(variance: number): string {
    if (variance === 0) return 'text-gray-600';
    return variance > 0 ? 'text-green-600 font-semibold' : 'text-red-600 font-semibold';
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric'
    });
  }
}
