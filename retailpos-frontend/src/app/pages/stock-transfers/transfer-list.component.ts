import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { AdvancedFilterDrawerComponent } from '../../components/advanced-filter-drawer/advanced-filter-drawer.component';
import { VariantSearchPickerComponent, VariantSearchItem } from '../../components/variant-search-picker/variant-search-picker.component';
import { StockTransferService } from '../../services/stock-transfer.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { AuthService } from '../../services/auth.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthorizedLocationDto, AuthorizedOutletsDto } from '../../models/report.model';
import {
  StockTransferDto,
  ReceiveStockTransferDto,
  StockTransferSearchRequest,
  getTransferTypeLabel,
  getTransferStatusLabel,
  getTransferStatusColor
} from '../../models/stock-transfer.model';
import { canDispatchTransferFromSource, canManageTransferFromSource, canReceiveTransferAtDestination } from '../../utils/stock-transfer-access.util';

@Component({
  selector: 'app-transfer-list',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule, AdvancedFilterDrawerComponent, VariantSearchPickerComponent],
  templateUrl: './transfer-list.component.html',
  styleUrls: ['./transfer-list.component.css']
})
export class TransferListComponent implements OnInit, OnDestroy {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  Math = Math;
  authorizedAccess = signal<AuthorizedOutletsDto | null>(null);
  sourceAccess = signal<AuthorizedOutletsDto | null>(null);

  searchTerm = signal<string>('');
  statusFilter = signal<string>('');
  pageNumber = signal<number>(1);
  pageSize = signal<number>(10);

  fromLocationType = signal<string>('');
  fromLocationId = signal<number | undefined>(undefined);
  toLocationType = signal<string>('');
  toLocationId = signal<number | undefined>(undefined);
  productVariantId = signal<number | undefined>(undefined);
  productVariantLabel = signal<string>('');
  transferTypeFilter = signal<string>('');
  dateFrom = signal<string>('');
  dateTo = signal<string>('');

  isAdvancedFilterDrawerOpen = signal<boolean>(false);
  draftFromLocationType = signal<string>('');
  draftFromLocationId = signal<number | undefined>(undefined);
  draftToLocationType = signal<string>('');
  draftToLocationId = signal<number | undefined>(undefined);
  draftProductVariantId = signal<number | undefined>(undefined);
  draftProductVariantLabel = signal<string>('');
  draftSelectedVariant = signal<VariantSearchItem | null>(null);
  draftTransferTypeFilter = signal<string>('');
  draftDateFrom = signal<string>('');
  draftDateTo = signal<string>('');

  showReasonModal = signal(false);
  reasonModalTitle = signal('');
  reasonText = signal('');
  private pendingAction?: (reason: string) => void;

  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public transferService: StockTransferService,
    public auth: AuthService,
    private userOutletAccess: UserOutletAccessService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.searchTerm.set(this.listState.str(p, 'search'));
    this.statusFilter.set(this.listState.str(p, 'status'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.fromLocationType.set(this.listState.str(p, 'fromType'));
    this.fromLocationId.set(this.listState.optionalId(p, 'fromId'));
    this.toLocationType.set(this.listState.str(p, 'toType'));
    this.toLocationId.set(this.listState.optionalId(p, 'toId'));
    this.productVariantId.set(this.listState.optionalId(p, 'variantId'));
    this.productVariantLabel.set(this.productVariantId() ? `Variant #${this.productVariantId()}` : '');
    this.transferTypeFilter.set(this.listState.str(p, 'transferType'));
    this.dateFrom.set(this.listState.str(p, 'dateFrom'));
    this.dateTo.set(this.listState.str(p, 'dateTo'));
    this.syncAdvancedDraftsFromApplied();
    this.setupDebouncedSearch();
    this.loadDefaultLocation();
    this.performSearch();
  }

  private loadDefaultLocation(): void {
    this.userOutletAccess.load().subscribe({
      next: (response) => {
        this.authorizedAccess.set((response?.data ?? null) as AuthorizedOutletsDto | null);
      },
      error: () => {
        this.authorizedAccess.set(null);
      }
    });

    this.userOutletAccess.load('StockTransferSource').subscribe({
      next: (response) => {
        this.sourceAccess.set((response?.data ?? null) as AuthorizedOutletsDto | null);
      },
      error: () => {
        this.sourceAccess.set(null);
      }
    });
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      search: this.searchTerm() || undefined,
      status: this.statusFilter() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize(),
      fromType: this.fromLocationType() || undefined,
      fromId: this.fromLocationId(),
      toType: this.toLocationType() || undefined,
      toId: this.toLocationId(),
      variantId: this.productVariantId(),
      transferType: this.transferTypeFilter() || undefined,
      dateFrom: this.dateFrom() || undefined,
      dateTo: this.dateTo() || undefined,
    });
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(debounceTime(400))
      .subscribe(() => {
        this.syncUrl();
        this.performSearch();
      });
  }

  onFilterChange(): void {
    this.pageNumber.set(1);
    this.searchSubject.next();
  }

  onSearchTermChange(): void {
    this.pageNumber.set(1);
    this.searchSubject.next();
  }

  private performSearch(): void {
    const req: StockTransferSearchRequest = {
      searchTerm: this.searchTerm() || undefined,
      status: this.statusFilter() || undefined,
      fromLocationType: this.fromLocationType() || undefined,
      fromLocationId: this.fromLocationId(),
      toLocationType: this.toLocationType() || undefined,
      toLocationId: this.toLocationId(),
      productVariantId: this.productVariantId(),
      transferType: this.transferTypeFilter() || undefined,
      dateFrom: this.dateFrom() || undefined,
      dateTo: this.dateTo() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    };
    this.transferService.search(req).subscribe({
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  search(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set('');
    this.fromLocationType.set('');
    this.fromLocationId.set(undefined);
    this.toLocationType.set('');
    this.toLocationId.set(undefined);
    this.productVariantId.set(undefined);
    this.productVariantLabel.set('');
    this.transferTypeFilter.set('');
    this.dateFrom.set('');
    this.dateTo.set('');
    this.syncAdvancedDraftsFromApplied();
    this.pageNumber.set(1);
    this.listState.clear(this.route);
    this.performSearch();
  }

  openAdvancedFilters(): void {
    this.syncAdvancedDraftsFromApplied();
    this.isAdvancedFilterDrawerOpen.set(true);
  }

  closeAdvancedFilters(): void {
    this.isAdvancedFilterDrawerOpen.set(false);
    this.syncAdvancedDraftsFromApplied();
  }

  applyAdvancedFilters(): void {
    this.fromLocationType.set(this.draftFromLocationType());
    this.fromLocationId.set(this.draftFromLocationId());
    this.toLocationType.set(this.draftToLocationType());
    this.toLocationId.set(this.draftToLocationId());
    this.productVariantId.set(this.draftProductVariantId());
    this.productVariantLabel.set(this.draftProductVariantLabel());
    this.transferTypeFilter.set(this.draftTransferTypeFilter());
    this.dateFrom.set(this.draftDateFrom());
    this.dateTo.set(this.draftDateTo());
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
    this.isAdvancedFilterDrawerOpen.set(false);
  }

  clearAdvancedFilters(): void {
    this.draftFromLocationType.set('');
    this.draftFromLocationId.set(undefined);
    this.draftToLocationType.set('');
    this.draftToLocationId.set(undefined);
    this.draftProductVariantId.set(undefined);
    this.draftProductVariantLabel.set('');
    this.draftSelectedVariant.set(null);
    this.draftTransferTypeFilter.set('');
    this.draftDateFrom.set('');
    this.draftDateTo.set('');
    this.applyAdvancedFilters();
  }

  onDraftFromLocationTypeChange(): void {
    this.draftFromLocationId.set(undefined);
  }

  onDraftToLocationTypeChange(): void {
    this.draftToLocationId.set(undefined);
  }

  onDraftVariantSelected(variant: VariantSearchItem): void {
    this.draftSelectedVariant.set(variant);
    this.draftProductVariantId.set(variant.id);
    const variantSuffix = variant.variantDisplay?.trim() ? ` (${variant.variantDisplay.trim()})` : '';
    this.draftProductVariantLabel.set(`${variant.productName}${variantSuffix}`);
  }

  onDraftVariantCleared(): void {
    this.draftSelectedVariant.set(null);
    this.draftProductVariantId.set(undefined);
    this.draftProductVariantLabel.set('');
  }

  advancedFilterCount(): number {
    let count = 0;
    if (this.fromLocationType()) count += 1;
    if (this.fromLocationId()) count += 1;
    if (this.toLocationType()) count += 1;
    if (this.toLocationId()) count += 1;
    if (this.productVariantId()) count += 1;
    if (this.transferTypeFilter()) count += 1;
    if (this.dateFrom()) count += 1;
    if (this.dateTo()) count += 1;
    return count;
  }

  hasAdvancedFilters(): boolean {
    return this.advancedFilterCount() > 0;
  }

  clearSingleAdvancedFilter(key: string): void {
    switch (key) {
      case 'fromType':
        this.fromLocationType.set('');
        this.fromLocationId.set(undefined);
        break;
      case 'fromId':
        this.fromLocationId.set(undefined);
        break;
      case 'toType':
        this.toLocationType.set('');
        this.toLocationId.set(undefined);
        break;
      case 'toId':
        this.toLocationId.set(undefined);
        break;
      case 'variant':
        this.productVariantId.set(undefined);
        this.productVariantLabel.set('');
        break;
      case 'transferType':
        this.transferTypeFilter.set('');
        break;
      case 'dateFrom':
        this.dateFrom.set('');
        break;
      case 'dateTo':
        this.dateTo.set('');
        break;
      default:
        return;
    }

    this.pageNumber.set(1);
    this.syncAdvancedDraftsFromApplied();
    this.syncUrl();
    this.performSearch();
  }

  getLocationOptions(locationType: string, isDestination: boolean): AuthorizedLocationDto[] {
    const access = this.authorizedAccess();
    if (!access) return [];

    if (isDestination) {
      if (locationType === 'outlet') return access.destinationOutlets ?? access.outlets ?? [];
      if (locationType === 'warehouse') return access.destinationWarehouses ?? access.warehouses ?? [];
      return [];
    }

    if (locationType === 'outlet') return access.outlets ?? [];
    if (locationType === 'warehouse') return access.warehouses ?? [];
    return [];
  }

  getActiveAdvancedFilterChips(): { key: string; label: string }[] {
    const chips: { key: string; label: string }[] = [];

    if (this.fromLocationType()) {
      chips.push({ key: 'fromType', label: `From Type: ${this.fromLocationType()}` });
    }

    if (this.fromLocationId()) {
      chips.push({ key: 'fromId', label: `From ID: ${this.fromLocationId()}` });
    }

    if (this.toLocationType()) {
      chips.push({ key: 'toType', label: `To Type: ${this.toLocationType()}` });
    }

    if (this.toLocationId()) {
      chips.push({ key: 'toId', label: `To ID: ${this.toLocationId()}` });
    }

    if (this.productVariantId()) {
      chips.push({
        key: 'variant',
        label: this.productVariantLabel() || `Variant #${this.productVariantId()}`
      });
    }

    if (this.transferTypeFilter()) {
      chips.push({ key: 'transferType', label: `Type: ${this.getTypeLabel(this.transferTypeFilter())}` });
    }

    if (this.dateFrom()) {
      chips.push({ key: 'dateFrom', label: `Date From: ${this.companyDates.format(this.dateFrom())}` });
    }

    if (this.dateTo()) {
      chips.push({ key: 'dateTo', label: `Date To: ${this.companyDates.format(this.dateTo())}` });
    }

    return chips;
  }

  private syncAdvancedDraftsFromApplied(): void {
    this.draftFromLocationType.set(this.fromLocationType());
    this.draftFromLocationId.set(this.fromLocationId());
    this.draftToLocationType.set(this.toLocationType());
    this.draftToLocationId.set(this.toLocationId());
    this.draftProductVariantId.set(this.productVariantId());
    this.draftProductVariantLabel.set(this.productVariantLabel());
    this.draftSelectedVariant.set(null);
    this.draftTransferTypeFilter.set(this.transferTypeFilter());
    this.draftDateFrom.set(this.dateFrom());
    this.draftDateTo.set(this.dateTo());
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.syncUrl();
    this.performSearch();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  createTransfer(): void {
    if (!this.auth.hasPermission('stock_transfers.create')) {
      this.alertService.error('You do not have permission to create stock transfers');
      return;
    }
    this.router.navigate(['/stock-transfers/create']);
  }

  viewTransfer(id: number): void {
    this.router.navigate(['/stock-transfers', id]);
  }

  approveTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm('Approve this stock transfer?', () => {
      this.transferService.approve(id).subscribe({
        next: () => {
          this.alertService.success('Transfer approved');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  rejectTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.openReasonModal('Reject Transfer', (reason) => {
      this.transferService.reject(id, { reason }).subscribe({
        next: () => {
          this.alertService.success('Transfer rejected');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  sendTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.alertService.confirm('Mark this transfer as in-transit?', () => {
      this.transferService.send(id).subscribe({
        next: () => {
          this.alertService.success('Transfer marked as in-transit');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  receiveTransfer(id: number, event: Event): void {
    event.stopPropagation();
    const transfer = this.transferService.transfers().find(t => t.id === id);
    if (!transfer) {
      this.alertService.error('Transfer details not found for receiving');
      return;
    }

    const payload = this.buildFullReceivePayload(transfer);
    this.alertService.confirm('Receive this transfer and update stock?', () => {
      this.transferService.receive(id, payload).subscribe({
        next: () => {
          this.alertService.success('Transfer received and stock updated');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  cancelTransfer(id: number, event: Event): void {
    event.stopPropagation();
    this.openReasonModal('Cancel Transfer', (reason) => {
      this.transferService.cancel(id, { reason }).subscribe({
        next: () => {
          this.alertService.success('Transfer cancelled');
          this.performSearch();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  private openReasonModal(title: string, action: (reason: string) => void): void {
    this.reasonModalTitle.set(title);
    this.reasonText.set('');
    this.pendingAction = action;
    this.showReasonModal.set(true);
  }

  submitReason(): void {
    if (!this.reasonText().trim()) {
      this.alertService.error('Please enter a reason');
      return;
    }
    this.showReasonModal.set(false);
    if (this.pendingAction) {
      this.pendingAction(this.reasonText().trim());
      this.pendingAction = undefined;
    }
  }

  closeReasonModal(): void {
    this.showReasonModal.set(false);
    this.pendingAction = undefined;
  }

  getStatusLabel(status: string): string {
    return getTransferStatusLabel(status);
  }

  getTypeLabel(type: string): string {
    return getTransferTypeLabel(type);
  }

  getStatusColor(status: string): string {
    return getTransferStatusColor(status);
  }

  formatDate(date: string): string {
    if (!date) return '-';
    return this.companyDates.format(date, false);
  }

  canApproveTransfer(transfer: StockTransferDto): boolean {
    return canManageTransferFromSource(transfer, this.sourceAccess());
  }

  canCancelTransfer(transfer: StockTransferDto): boolean {
    return canManageTransferFromSource(transfer, this.sourceAccess());
  }

  canSendTransfer(transfer: StockTransferDto): boolean {
    return canDispatchTransferFromSource(
      transfer,
      this.authorizedAccess(),
      this.auth.hasPermission('stock_transfers.transfer_from_any_location'),
      this.sourceAccess()
    );
  }

  canReceiveOrRejectTransfer(transfer: StockTransferDto): boolean {
    return canReceiveTransferAtDestination(transfer, this.authorizedAccess());
  }

  private buildFullReceivePayload(transfer: StockTransferDto): ReceiveStockTransferDto {
    return {
      notes: 'Full acceptance from list action',
      items: transfer.items.map(item => ({
        variantId: item.variantId,
        acceptedQuantity: item.transferQuantity > 0 ? item.transferQuantity : item.quantity,
        rejectedQuantity: 0,
        remarks: item.remarks
      }))
    };
  }
}
