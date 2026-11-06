import { Component, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { BusinessService } from '../../services/business.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { CreateBusinessRequestDto } from '../../models/business.model';
import { BusinessReceiptFooterFieldsComponent } from './business-receipt-footer-fields.component';

@Component({
  selector: 'app-business-form',
  standalone: true,
  imports: [CommonModule, FormsModule, BusinessReceiptFooterFieldsComponent],
  templateUrl: './business-form.component.html'
})
export class BusinessFormComponent {
  isSubmitting = signal(false);
  showPoweredBy = signal(false);
  poweredByText = signal('');

  model: CreateBusinessRequestDto = {
    businessName: '',
    businessEmail: '',
    businessPhone: '',
    businessAddress: '',

    ownerName: '',
    ownerEmail: '',

    outletManagerName: '',
    outletManagerEmail: '',

    salesPersonName: '',
    salesPersonEmail: '',

    accountsAdminName: '',
    accountsAdminEmail: '',

    defaultOutletName: 'Main Outlet',

    createDefaultWarehouse: false,
    defaultWarehouseName: 'Main Warehouse',
    warehouseManagerName: '',
    warehouseManagerEmail: ''
  };

  constructor(
    private businessService: BusinessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private router: Router,
    private location: Location,
    private authService: AuthService
  ) {}

  get canManagePoweredBy(): boolean {
    return this.authService.isSuperAdmin();
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);

    const request: CreateBusinessRequestDto = this.canManagePoweredBy
      ? {
          ...this.model,
          showPoweredByOnReceipt: this.showPoweredBy(),
          poweredByText: this.poweredByText().trim() || null
        }
      : this.model;

    this.businessService.createBusiness(request).subscribe({
      next: (res) => {
        this.isSubmitting.set(false);
        this.alertService.success(res.message || 'Business created successfully');
        this.location.back();
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  cancel(): void {
    this.location.back();
  }
}
