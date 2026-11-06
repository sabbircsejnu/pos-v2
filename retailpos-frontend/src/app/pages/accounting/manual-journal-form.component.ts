import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateJournalEntryDto, JournalEntryAttachmentDto, JournalEntryLineDto, UpdateJournalEntryDto } from '../../models/accounting.model';

@Component({
  selector: 'app-manual-journal-form',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './manual-journal-form.component.html',
  styleUrls: ['./manual-journal-form.component.css']
})
export class ManualJournalFormComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  isEditMode = signal(false);
  journalId = signal<number | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);
  description = signal('');
  transactionDate = signal(this.companyToday.todayIso());
  postingDate = signal(this.companyToday.todayIso());
  businessDate = signal(this.companyToday.todayIso());
  referenceNumber = signal('');
  referenceType = signal('');
  referenceId = signal('');
  lines = signal<JournalEntryLineDto[]>([
    { accountId: 0, debitAmount: 0, creditAmount: 0 },
    { accountId: 0, debitAmount: 0, creditAmount: 0 }
  ]);
  attachments = signal<JournalEntryAttachmentDto[]>([]);

  constructor(
    public accountingService: AccountingService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.accountingService.getAccounts().subscribe();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.journalId.set(+id);
      this.load(+id);
    }
  }

  load(id: number): void {
    this.isLoading.set(true);
    this.accountingService.getJournalById(id).subscribe({
      next: (res) => {
        const item = res.data;
        if (item) {
          this.description.set(item.description || '');
          this.transactionDate.set(item.transactionDate.split('T')[0]);
          this.postingDate.set(item.postingDate.split('T')[0]);
          this.businessDate.set((item.businessDate || item.postingDate).split('T')[0]);
          this.referenceNumber.set(item.referenceNumber || '');
          this.referenceType.set(item.referenceType || '');
          this.referenceId.set(item.referenceId || '');
          this.lines.set(item.lines.map((x: JournalEntryLineDto) => ({ ...x })));
          this.attachments.set(item.attachments || []);
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  addLine(): void {
    this.lines.update(x => [...x, { accountId: 0, debitAmount: 0, creditAmount: 0 }]);
  }

  removeLine(index: number): void {
    this.lines.update(x => x.length <= 2 ? x : x.filter((_, i) => i !== index));
  }

  updateLine(index: number, patch: Partial<JournalEntryLineDto>): void {
    this.lines.update(items => items.map((item, i) => i === index ? { ...item, ...patch } : item));
  }

  onSubmit(): void {
    const dto: CreateJournalEntryDto | UpdateJournalEntryDto = {
      journalType: 'ManualJournal',
      transactionDate: this.transactionDate(),
      postingDate: this.postingDate(),
      businessDate: this.businessDate(),
      referenceType: this.referenceType() || undefined,
      referenceId: this.referenceId() || undefined,
      referenceNumber: this.referenceNumber() || undefined,
      description: this.description(),
      lines: this.lines()
    };

    this.isSubmitting.set(true);
    const request = this.isEditMode()
      ? this.accountingService.updateManualJournal(this.journalId()!, dto as UpdateJournalEntryDto)
      : this.accountingService.createManualJournal(dto as CreateJournalEntryDto);

    request.subscribe({
      next: (res) => {
        const id = res.data?.id || this.journalId();
        this.alertService.success(this.isEditMode() ? 'Journal updated successfully' : 'Journal created successfully');
        if (id) {
          this.router.navigate(['/manual-journals', id]);
          return;
        }
        this.location.back();
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.journalId()) return;
    this.accountingService.uploadJournalAttachment(this.journalId()!, file).subscribe({
      next: () => {
        this.alertService.success('Attachment uploaded successfully');
        this.load(this.journalId()!);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  downloadAttachment(att: JournalEntryAttachmentDto): void {
    if (!this.journalId()) return;
    this.accountingService.downloadJournalAttachment(this.journalId()!, att.id).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = att.fileName;
        anchor.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  deleteAttachment(att: JournalEntryAttachmentDto): void {
    if (!this.journalId()) return;
    this.accountingService.deleteJournalAttachment(this.journalId()!, att.id).subscribe({
      next: () => {
        this.alertService.success('Attachment deleted successfully');
        this.load(this.journalId()!);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  onCancel(): void { this.location.back(); }
}