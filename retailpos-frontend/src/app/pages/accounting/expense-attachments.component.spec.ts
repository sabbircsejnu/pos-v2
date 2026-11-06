import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ExpenseAttachmentsComponent } from './expense-attachments.component';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

describe('ExpenseAttachmentsComponent', () => {
  const stored = { id: 7, fileName: 'receipt.pdf', contentType: 'application/pdf', fileSizeBytes: 2048, createdAt: '' };
  let fixture: ComponentFixture<ExpenseAttachmentsComponent>;
  let component: ExpenseAttachmentsComponent;
  let accounting: Record<string, ReturnType<typeof vi.fn>>;
  let alerts: { confirm: ReturnType<typeof vi.fn> };

  async function setup(expenseId: number | null, editable: boolean): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ExpenseAttachmentsComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'Server said no' } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ExpenseAttachmentsComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('expenseId', expenseId);
    fixture.componentRef.setInput('editable', editable);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const button = (label: string) => (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);

  beforeEach(() => {
    accounting = {
      getExpenseAttachments: vi.fn().mockReturnValue(of({ data: [stored] })),
      uploadExpenseAttachment: vi.fn(),
      deleteExpenseAttachment: vi.fn().mockReturnValue(of({})),
      getExpenseAttachmentContent: vi.fn()
    };
    alerts = { confirm: vi.fn((_: string, onConfirm: () => void) => onConfirm()) };
  });

  it('queues files until the expense exists and refuses other types locally', async () => {
    await setup(null, true);

    component.addFiles([new File(['x'], 'a.pdf'), new File(['x'], 'b.WEBP'), new File(['x'], 'c.exe'), new File(['x'], 'd.svg')]);

    expect(component.pendingFiles().map(file => file.name)).toEqual(['a.pdf', 'b.WEBP']);
    expect(component.actionError()).toContain('Only PDF');
    expect(accounting['uploadExpenseAttachment']).not.toHaveBeenCalled();
    expect(accounting['getExpenseAttachments']).not.toHaveBeenCalled();
  });

  it('uploads immediately for a saved expense and reports a refused file', async () => {
    await setup(9, true);
    accounting['uploadExpenseAttachment'].mockReturnValue(throwError(() => new Error('too big')));

    component.addFiles([new File(['x'], 'big.pdf')]);

    expect(accounting['uploadExpenseAttachment']).toHaveBeenCalledWith(9, expect.any(File));
    expect(component.actionError()).toContain('big.pdf: Server said no');
    expect(component.isUploading()).toBe(false);
  });

  it('is read-only when not editable', async () => {
    await setup(9, false);

    expect(button('Preview receipt.pdf')).not.toBeNull();
    expect(button('Download receipt.pdf')).not.toBeNull();
    expect(button('Remove receipt.pdf')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('input[type="file"]')).toBeNull();
  });

  it('removes after confirmation and keeps the row when the server refuses', async () => {
    await setup(9, true);

    accounting['deleteExpenseAttachment'].mockReturnValueOnce(throwError(() => new Error('approved')));
    component.remove(stored);
    expect(component.attachments()).toHaveLength(1);
    expect(component.actionError()).toBe('Server said no');

    component.remove(stored);
    expect(accounting['deleteExpenseAttachment']).toHaveBeenLastCalledWith(9, 7);
    expect(component.attachments()).toHaveLength(0);
  });

  it('shows a retryable error when the list cannot load', async () => {
    accounting['getExpenseAttachments'].mockReturnValueOnce(throwError(() => new Error('down')));
    await setup(9, true);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Could not load attachments');

    component.retry();
    expect(component.attachments()).toEqual([stored]);
  });

  it('closes the preview tab when the file cannot be fetched', async () => {
    await setup(9, false);
    const tab = { close: vi.fn(), location: { href: '' } };
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
    accounting['getExpenseAttachmentContent'].mockReturnValue(throwError(() => new Error('403')));

    component.preview(stored);

    expect(accounting['getExpenseAttachmentContent']).toHaveBeenCalledWith(9, 7, true);
    expect(tab.close).toHaveBeenCalled();
    expect(component.actionError()).toBe('Server said no');
    open.mockRestore();
  });
});
