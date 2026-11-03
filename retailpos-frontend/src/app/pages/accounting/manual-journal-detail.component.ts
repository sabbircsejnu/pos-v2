import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { AuthService } from '../../services/auth.service';
import { JournalEntryAttachmentDto, JournalEntryDto } from '../../models/accounting.model';

@Component({
  selector: 'app-manual-journal-detail',
  standalone: true,
  imports: [AppDatePipe, CommonModule],
  templateUrl: './manual-journal-detail.component.html',
  styleUrls: ['./manual-journal-detail.component.css']
})
export class ManualJournalDetailComponent implements OnInit {
  journal = signal<JournalEntryDto | null>(null);
  isLoading = signal(false);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    public accountingService: AccountingService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.load(id);
  }

  load(id: number): void {
    this.isLoading.set(true);
    this.accountingService.getJournalById(id).subscribe({
      next: (res) => { this.journal.set(res.data || null); this.isLoading.set(false); },
      error: (err) => { this.alertService.error(this.errorHandler.extractErrorMessage(err)); this.isLoading.set(false); }
    });
  }

  edit(): void { if (this.journal()) this.router.navigate(['/manual-journals', this.journal()!.id, 'edit']); }
  back(): void { this.location.back(); }

  download(att: JournalEntryAttachmentDto): void {
    const item = this.journal(); if (!item) return;
    this.accountingService.downloadJournalAttachment(item.id, att.id).subscribe({
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
}