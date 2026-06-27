import { Component, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { BusinessService } from '../../services/business.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateBusinessRequestDto } from '../../models/business.model';

@Component({
  selector: 'app-business-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './business-form.component.html'
})
export class BusinessFormComponent {
  isSubmitting = signal(false);

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
    private location: Location
  ) {}

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);

    this.businessService.createBusiness(this.model).subscribe({
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
