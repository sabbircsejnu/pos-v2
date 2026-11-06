import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, concatMap, from, map, of, toArray } from 'rxjs';
import { UiAlertComponent, UiButtonComponent, UiStateComponent } from '../../components/shared/ui';
import { ExpenseAttachmentDto } from '../../models/accounting.model';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

const ACCEPTED_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png', '.webp'];

/**
 * An expense's supporting documents. Before the expense exists, chosen files wait here and the form uploads them
 * right after it is created. Type and size are decided by the server; the check here only saves a pointless upload.
 */
@Component({
  selector: 'app-expense-attachments',
  standalone: true,
  imports: [UiAlertComponent, UiButtonComponent, UiStateComponent],
  templateUrl: './expense-attachments.component.html',
  styleUrls: ['./expense-attachments.component.css']
})
export class ExpenseAttachmentsComponent {
  readonly expenseId = input<number | null>(null);
  readonly editable = input(false);

  readonly accept = ACCEPTED_EXTENSIONS.join(',');
  readonly attachments = signal<ExpenseAttachmentDto[]>([]);
  readonly pendingFiles = signal<File[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly isUploading = signal(false);

  private readonly accounting = inject(AccountingService);
  private readonly alerts = inject(AlertService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    effect(() => {
      const id = this.expenseId();
      if (id !== null) this.load(id);
    });
  }

  load(id: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.accounting.getExpenseAttachments(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: response => {
        if (this.expenseId() !== id) return;
        this.attachments.set(response.data ?? []);
        this.isLoading.set(false);
      },
      error: error => {
        if (this.expenseId() !== id) return;
        this.loadError.set(this.errors.extractErrorMessage(error));
        this.isLoading.set(false);
      }
    });
  }

  retry(): void {
    const id = this.expenseId();
    if (id !== null) this.load(id);
  }

  onFilesChosen(event: Event): void {
    const field = event.target as HTMLInputElement;
    this.addFiles(Array.from(field.files ?? []));
    field.value = '';
  }

  addFiles(files: File[]): void {
    this.actionError.set(null);
    const accepted = files.filter(file => ACCEPTED_EXTENSIONS.some(extension => file.name.toLowerCase().endsWith(extension)));
    if (accepted.length < files.length) {
      this.actionError.set('Only PDF, JPG, PNG and WebP files can be attached.');
    }

    const id = this.expenseId();
    if (id === null) {
      this.pendingFiles.update(pending => [...pending, ...accepted]);
    } else if (accepted.length > 0) {
      this.uploadFiles(id, accepted).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(failures => {
        if (failures.length > 0) this.actionError.set(failures.join(' '));
      });
    }
  }

  removePending(index: number): void {
    this.pendingFiles.update(pending => pending.filter((_, position) => position !== index));
  }

  /** Uploads the files waiting for a newly created expense; emits one message per file the server refused. */
  uploadPending(expenseId: number): Observable<string[]> {
    const files = this.pendingFiles();
    if (files.length === 0) return of([]);
    return this.uploadFiles(expenseId, files).pipe(map(failures => {
      this.pendingFiles.set([]);
      return failures;
    }));
  }

  preview(attachment: ExpenseAttachmentDto): void {
    const id = this.expenseId();
    if (id === null) return;

    // Opened before the request: a tab opened after an async call is treated as a pop-up.
    const tab = window.open('about:blank', '_blank');
    this.accounting.getExpenseAttachmentContent(id, attachment.id, true).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        if (tab) {
          tab.location.href = url;
        } else {
          this.actionError.set('Your browser blocked the preview. Allow pop-ups or use Download.');
        }
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: error => {
        tab?.close();
        this.actionError.set(this.errors.extractErrorMessage(error));
      }
    });
  }

  download(attachment: ExpenseAttachmentDto): void {
    const id = this.expenseId();
    if (id === null) return;

    this.accounting.getExpenseAttachmentContent(id, attachment.id, false).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = attachment.fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: error => this.actionError.set(this.errors.extractErrorMessage(error))
    });
  }

  remove(attachment: ExpenseAttachmentDto): void {
    const id = this.expenseId();
    if (id === null) return;

    this.alerts.confirm(`Remove ${attachment.fileName} from this expense?`, () => {
      this.actionError.set(null);
      this.accounting.deleteExpenseAttachment(id, attachment.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => this.attachments.update(rows => rows.filter(row => row.id !== attachment.id)),
        error: error => this.actionError.set(this.errors.extractErrorMessage(error))
      });
    }, 'Remove attachment', 'Remove');
  }

  formatSize(bytes: number): string {
    return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private uploadFiles(expenseId: number, files: File[]): Observable<string[]> {
    this.isUploading.set(true);
    return from(files).pipe(
      concatMap(file => this.accounting.uploadExpenseAttachment(expenseId, file).pipe(
        map(response => {
          this.attachments.update(rows => [...rows, response.data]);
          return null;
        }),
        catchError(error => of(`${file.name}: ${this.errors.extractErrorMessage(error)}`))
      )),
      toArray(),
      map(results => {
        this.isUploading.set(false);
        return results.filter((failure): failure is string => failure !== null);
      })
    );
  }
}
