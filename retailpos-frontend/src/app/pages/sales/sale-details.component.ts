import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { SaleService } from '../../services/sale.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { SaleDto } from '../../models/sale.model';

@Component({
  selector: 'app-sale-details',
  standalone: true,
  imports: [AppDatePipe, CommonModule, FormsModule, AppCurrencyPipe],
  templateUrl: './sale-details.component.html',
  styleUrls: ['./sale-details.component.css']
})
export class SaleDetailsComponent implements OnInit {
  sale = signal<SaleDto | null>(null);
  isLoading = signal(false);
  isVoiding = signal(false);
  showVoidModal = signal(false);
  voidReason = signal('');
  isPrinting = signal(false);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    public saleService: SaleService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loadSale(id);
  }

  private loadSale(id: number): void {
    this.isLoading.set(true);
    this.saleService.getSaleById(id).subscribe({
      next: (res) => {
        this.sale.set(res.data || res);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  canVoid(): boolean {
    const s = this.sale();
    if (!s || s.status?.toLowerCase() !== 'completed') return false;
    if (!this.authService.hasPermission('sales.void')) return false;
    const saleDate = new Date(s.saleDate);
    const today = new Date();
    return saleDate.toDateString() === today.toDateString();
  }

  openVoidModal(): void {
    this.voidReason.set('');
    this.showVoidModal.set(true);
  }

  closeVoidModal(): void {
    this.showVoidModal.set(false);
  }

  confirmVoid(): void {
    if (!this.voidReason().trim()) {
      this.alertService.error('Please enter a reason for voiding this sale');
      return;
    }
    const s = this.sale();
    if (!s) return;

    this.isVoiding.set(true);
    this.saleService.voidSale(s.id, { reason: this.voidReason() }).subscribe({
      next: () => {
        this.alertService.success('Sale voided successfully');
        this.showVoidModal.set(false);
        this.isVoiding.set(false);
        this.loadSale(s.id);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isVoiding.set(false);
      }
    });
  }

  /** Records the reprint on the server (print history, audited), then opens the same receipt as a PDF to print. */
  reprintReceipt(): void {
    const s = this.sale();
    if (!s || this.isPrinting()) return;
    this.isPrinting.set(true);
    this.saleService.reprintSaleReceipt(s.id).subscribe({
      next: () => this.openReceiptPdf(s.id, true),
      error: (err) => {
        this.isPrinting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  downloadReceiptPdf(): void {
    const s = this.sale();
    if (!s || this.isPrinting()) return;
    this.isPrinting.set(true);
    this.openReceiptPdf(s.id, false);
  }

  private openReceiptPdf(saleId: number, forPrinting: boolean): void {
    this.saleService.getReceiptPdf(saleId, '80mm').subscribe({
      next: (blob) => {
        this.isPrinting.set(false);
        const url = URL.createObjectURL(blob);
        if (forPrinting) {
          window.open(url, '_blank', 'noopener');
          setTimeout(() => URL.revokeObjectURL(url), 60_000);
          return;
        }
        const link = document.createElement('a');
        link.href = url;
        link.download = `receipt-${this.sale()?.saleNumber || saleId}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.isPrinting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  goBack(): void {
    this.location.back();
  }

  getStatusClass(status: string): string {
    switch (status?.toLowerCase()) {
      case 'completed': return 'status-completed';
      case 'void':
      case 'voided':
      case 'refunded': return 'status-void';
      default: return 'status-default';
    }
  }

  getPaymentIcon(method: string): string {
    switch (method?.toLowerCase()) {
      case 'cash': return 'fas fa-money-bill-wave';
      case 'card': return 'fas fa-credit-card';
      case 'mobile': return 'fas fa-mobile-alt';
      default: return 'fas fa-dollar-sign';
    }
  }
}
