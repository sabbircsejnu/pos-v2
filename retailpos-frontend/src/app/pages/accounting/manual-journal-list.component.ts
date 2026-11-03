import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ListStateService } from '../../services/list-state.service';
import { AuthService } from '../../services/auth.service';
import { JournalEntryDto } from '../../models/accounting.model';

@Component({
  selector: 'app-manual-journal-list',
  standalone: true,
  imports: [AppDatePipe, CommonModule, FormsModule],
  templateUrl: './manual-journal-list.component.html',
  styleUrls: ['./manual-journal-list.component.css']
})
export class ManualJournalListComponent implements OnInit {
  filterStatus = signal('');
  pageNumber = signal(1);
  pageSize = signal(10);
  readonly statuses = ['Draft', 'Submitted', 'Approved', 'Rejected', 'Cancelled', 'Posted', 'Reversed'];

  constructor(
    public accountingService: AccountingService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParams;
    this.filterStatus.set(this.listState.str(p, 'status'));
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.load();
  }

  load(): void {
    this.accountingService.searchJournals({
      journalType: 'ManualJournal',
      status: this.filterStatus() || undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize()
    }).subscribe();
  }

  applyFilters(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.load();
  }

  clearFilters(): void {
    this.filterStatus.set('');
    this.pageNumber.set(1);
    this.listState.clear(this.route);
    this.load();
  }

  syncUrl(): void {
    this.listState.update(this.route, {
      status: this.filterStatus() || undefined,
      page: this.pageNumber(),
      pageSize: this.pageSize()
    });
  }

  create(): void {
    this.router.navigate(['/manual-journals/create']);
  }

  open(item: JournalEntryDto): void {
    this.router.navigate(['/manual-journals', item.id]);
  }

  submit(item: JournalEntryDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.submitManualJournal(item.id).subscribe({
      next: () => { this.alertService.success('Journal submitted successfully'); this.load(); },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  approve(item: JournalEntryDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.approveManualJournal(item.id).subscribe({
      next: () => { this.alertService.success('Journal approved successfully'); this.load(); },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  reject(item: JournalEntryDto, event: Event): void {
    event.stopPropagation();
    const reason = window.prompt('Enter rejection reason')?.trim();
    if (!reason) return;
    this.accountingService.rejectManualJournal(item.id, reason).subscribe({
      next: () => { this.alertService.success('Journal rejected successfully'); this.load(); },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  cancel(item: JournalEntryDto, event: Event): void {
    event.stopPropagation();
    const reason = window.prompt('Enter cancellation reason (optional)')?.trim();
    this.accountingService.cancelManualJournal(item.id, reason || undefined).subscribe({
      next: () => { this.alertService.success('Journal cancelled successfully'); this.load(); },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  post(item: JournalEntryDto, event: Event): void {
    event.stopPropagation();
    this.accountingService.postManualJournal(item.id).subscribe({
      next: () => { this.alertService.success('Journal posted successfully'); this.load(); },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  canSubmit(item: JournalEntryDto): boolean { return (item.status === 'Draft' || item.status === 'Rejected') && this.auth.hasPermission('accounting.manual_journals.submit'); }
  canApprove(item: JournalEntryDto): boolean { return item.status === 'Submitted' && this.auth.hasPermission('accounting.manual_journals.approve'); }
  canReject(item: JournalEntryDto): boolean { return item.status === 'Submitted' && this.auth.hasPermission('accounting.manual_journals.reject'); }
  canCancel(item: JournalEntryDto): boolean { return ['Draft', 'Rejected', 'Submitted', 'Approved'].includes(item.status) && this.auth.hasPermission('accounting.manual_journals.cancel'); }
  canPost(item: JournalEntryDto): boolean { return ['Draft', 'Approved'].includes(item.status) && this.auth.hasPermission('accounting.manual_journals.post'); }
}