import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { StockCountService } from '../../services/stock-count.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthorizedLocationDto } from '../../models/report.model';

@Component({
  selector: 'app-stock-count-create',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-count-create.component.html',
  styleUrls: ['./stock-count-create.component.css']
})
export class StockCountCreateComponent implements OnInit {
  isLoading = signal(false);
  isSubmitting = signal(false);
  canSelectAnyLocation = signal(false);

  stockCountDate = signal(new Date().toISOString().slice(0, 10));
  remarks = signal('');
  locationType = signal<'outlet' | 'warehouse'>('outlet');
  locationId = signal<number | null>(null);

  outlets = signal<AuthorizedLocationDto[]>([]);
  warehouses = signal<AuthorizedLocationDto[]>([]);

  constructor(
    public auth: AuthService,
    private service: StockCountService,
    private locationAccess: UserOutletAccessService,
    private alert: AlertService,
    private errorHandler: ErrorHandlerService,
    private router: Router,
    private location: Location,
  ) {}

  ngOnInit(): void {
    this.isLoading.set(true);
    this.locationAccess.load().subscribe({
      next: (res) => {
        const data = res.data;
        this.outlets.set(data?.outlets || []);
        this.warehouses.set(data?.warehouses || []);
        this.canSelectAnyLocation.set(!!data?.isGlobalAccess || this.auth.hasPermission('StockCount.ViewAll'));

        if (data?.defaultLocationType === 'warehouse') {
          this.locationType.set('warehouse');
        } else {
          this.locationType.set('outlet');
        }

        this.locationId.set(data?.defaultLocationId ?? null);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alert.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  onLocationTypeChange(type: 'outlet' | 'warehouse'): void {
    this.locationType.set(type);
    this.locationId.set(null);
  }

  getLocations(): AuthorizedLocationDto[] {
    return this.locationType() === 'warehouse' ? this.warehouses() : this.outlets();
  }

  submit(): void {
    if (!this.locationId()) {
      this.alert.error('Please select a valid location');
      return;
    }

    this.isSubmitting.set(true);

    this.service.create({
      stockCountDate: this.stockCountDate(),
      locationId: this.locationId()!,
      locationType: this.locationType(),
      remarks: this.remarks() || undefined,
    }).subscribe({
      next: (res) => {
        this.alert.success('Stock count generated successfully');
        this.isSubmitting.set(false);
        this.router.navigate(['/stock-counts', res.data.id]);
      },
      error: (err) => {
        this.alert.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  goBack(): void {
    this.location.back();
  }
}
