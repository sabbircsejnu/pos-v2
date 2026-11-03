import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { OutletService } from '../../services/outlet.service';
import { UserService } from '../../services/user.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { User } from '../../models/user.model';
import { CreateOutletRequest, Outlet, OutletTerminal, OutletTerminalInput } from '../../models/outlet.model';
import { COMMON_TIME_ZONES } from '../../models/settings.model';
import { UI_KIT } from '../shared/ui';

export const INVOICE_PREFIX_PATTERN = /^[A-Za-z][A-Za-z0-9]{0,9}$/;
export const TERMINAL_NAME_MAX_LENGTH = 120;
export const DEFAULT_TERMINAL_NAME = 'Counter 1';

export type TerminalGroup = FormGroup<{ name: FormControl<string>; isActive: FormControl<boolean> }>;

/** One editable terminal row; the persisted facts (code, history, open session) come from the API and never change locally. */
export interface TerminalRow {
  group: TerminalGroup;
  id: number | null;
  code: string | null;
  hasHistory: boolean;
  hasOpenSession: boolean;
}

interface FieldErrors {
  invoicePrefix?: string;
  timeZoneId?: string;
  terminals?: string;
}

function normalizeName(value: unknown): string {
  return String(value ?? '').trim().toLowerCase();
}

function uniqueTerminalNames(control: AbstractControl): ValidationErrors | null {
  const names = (control as FormArray).controls
    .map(group => normalizeName(group.get('name')?.value))
    .filter(name => name.length > 0);
  return new Set(names).size === names.length ? null : { duplicateTerminalNames: true };
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim().length === 0 ? { required: true } : null;
}

/** Error body of the outlet API; the global error interceptor keeps the original response in `originalError`. */
export function extractApiErrorCode(error: any): string | null {
  return error?.originalError?.error?.errorCode ?? error?.error?.errorCode ?? null;
}

@Component({
  selector: 'app-outlet-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ...UI_KIT],
  templateUrl: './outlet-form.component.html',
  styleUrls: ['./outlet-form.component.css']
})
export class OutletFormComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly alertService = inject(AlertService);

  readonly timeZoneSuggestions = COMMON_TIME_ZONES;
  readonly terminalNameMaxLength = TERMINAL_NAME_MAX_LENGTH;

  outletForm: FormGroup;
  terminalsArray = new FormArray<TerminalGroup>([], uniqueTerminalNames);
  terminalRows = signal<TerminalRow[]>([]);
  isEditMode = signal(false);
  outletId = signal<number | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  error = signal<string | null>(null);
  fieldErrors = signal<FieldErrors>({});
  managers = signal<User[]>([]);
  invoicePrefixLocked = signal(false);

  terminalToDelete = signal<TerminalRow | null>(null);
  deleteDialogOpen = signal(false);
  isDeletingTerminal = signal(false);

  constructor(
    private fb: FormBuilder,
    private outletService: OutletService,
    private userService: UserService,
    private router: Router,
    private location: Location,
    private route: ActivatedRoute
  ) {
    this.outletForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      address: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(200)]],
      contactNumber: ['', [Validators.pattern(/^[\d\s\-\+\(\)]+$/)]],
      managerId: [null],
      invoicePrefix: ['', [Validators.pattern(INVOICE_PREFIX_PATTERN)]],
      timeZoneId: [''],
      terminals: this.terminalsArray
    });
  }

  /** Changing terminals needs outlets.terminals.manage; without it the editor is read-only and `terminals` is not sent. */
  get canManageTerminals(): boolean {
    return this.auth.hasPermission('outlets.terminals.manage');
  }

  get activeTerminalCount(): number {
    return this.terminalRows().filter(row => row.group.controls.isActive.value).length;
  }

  /** Example invoice number: prefix + yyMMdd + sequence, rendered without separators. */
  get invoiceExample(): string {
    const control = this.outletForm.get('invoicePrefix');
    const prefix = control?.valid && control.value ? String(control.value).toUpperCase() : 'DHK01';
    const now = new Date();
    const yy = String(now.getFullYear() % 100).padStart(2, '0');
    const mm = String(now.getMonth() + 1).padStart(2, '0');
    const dd = String(now.getDate()).padStart(2, '0');
    return `${prefix}${yy}${mm}${dd}1`;
  }

  ngOnInit(): void {
    this.loadManagers();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.outletId.set(+id);
      this.loadOutlet(+id);
    } else if (this.canManageTerminals) {
      // Pre-filled for convenience only: nothing is persisted until the outlet is saved.
      this.addTerminal(DEFAULT_TERMINAL_NAME);
    }
  }

  loadManagers(): void {
    this.userService.getUsers().subscribe({
      next: (response: any) => {
        this.managers.set(response.data || []);
      },
      error: (error: any) => {
        console.error('Error loading managers:', error);
      }
    });
  }

  loadOutlet(id: number): void {
    this.isLoading.set(true);
    this.outletService.getOutletById(id).subscribe({
      next: (response: any) => {
        const outlet: Outlet = response.data;
        this.outletForm.patchValue({
          name: outlet.name,
          address: outlet.address,
          contactNumber: outlet.contactNumber,
          managerId: outlet.managerId,
          invoicePrefix: outlet.invoicePrefix ?? '',
          timeZoneId: outlet.timeZoneId ?? ''
        });
        this.setInvoicePrefixLocked(!!outlet.invoicePrefixLocked);
        this.setTerminalRows((outlet.terminals ?? []).map(terminal => this.createRow(terminal)));
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading outlet:', error);
        this.error.set('Failed to load outlet');
        this.isLoading.set(false);
      }
    });
  }

  private setInvoicePrefixLocked(locked: boolean): void {
    this.invoicePrefixLocked.set(locked);
    const control = this.outletForm.get('invoicePrefix')!;
    if (locked) {
      control.disable({ emitEvent: false });
    } else {
      control.enable({ emitEvent: false });
    }
  }

  private createRow(terminal: Partial<OutletTerminal> & { name: string }): TerminalRow {
    const row: TerminalRow = {
      group: this.fb.nonNullable.group({
        name: [terminal.name, [Validators.required, Validators.maxLength(TERMINAL_NAME_MAX_LENGTH), notBlank]],
        isActive: [terminal.isActive ?? true]
      }) as TerminalGroup,
      id: terminal.id ?? null,
      code: terminal.code ?? null,
      hasHistory: !!terminal.hasHistory,
      hasOpenSession: !!terminal.hasOpenSession
    };
    if (!this.canManageTerminals) {
      row.group.disable({ emitEvent: false });
    } else if (row.hasOpenSession) {
      // Deactivating a terminal with an open or overdue cash session is refused by the API.
      row.group.controls.isActive.disable({ emitEvent: false });
    }
    return row;
  }

  private setTerminalRows(rows: TerminalRow[]): void {
    this.terminalsArray.clear({ emitEvent: false });
    rows.forEach(row => this.terminalsArray.push(row.group, { emitEvent: false }));
    this.terminalsArray.updateValueAndValidity();
    this.terminalRows.set(rows);
  }

  addTerminal(name = ''): void {
    if (!this.canManageTerminals) return;
    this.setTerminalRows([...this.terminalRows(), this.createRow({ name })]);
    this.clearFieldError('terminals');
  }

  /** Unsaved rows can simply be dropped; persisted rows are deactivated or deleted through the API instead. */
  removeUnsavedTerminal(row: TerminalRow): void {
    if (row.id !== null || !this.canManageTerminals) return;
    this.setTerminalRows(this.terminalRows().filter(candidate => candidate !== row));
    this.clearFieldError('terminals');
  }

  canDeleteTerminal(row: TerminalRow): boolean {
    return this.canManageTerminals && this.isEditMode() && row.id !== null && !row.hasHistory;
  }

  requestDeleteTerminal(row: TerminalRow): void {
    if (!this.canDeleteTerminal(row)) return;
    this.terminalToDelete.set(row);
    this.deleteDialogOpen.set(true);
  }

  cancelDeleteTerminal(): void {
    if (this.isDeletingTerminal()) return;
    this.terminalToDelete.set(null);
    this.deleteDialogOpen.set(false);
  }

  confirmDeleteTerminal(): void {
    const row = this.terminalToDelete();
    const outletId = this.outletId();
    if (!row || row.id === null || outletId === null || this.isDeletingTerminal()) return;

    this.isDeletingTerminal.set(true);
    this.outletService.deleteTerminal(outletId, row.id).subscribe({
      next: () => {
        this.setTerminalRows(this.terminalRows().filter(candidate => candidate !== row));
        this.isDeletingTerminal.set(false);
        this.terminalToDelete.set(null);
        this.deleteDialogOpen.set(false);
        this.clearFieldError('terminals');
      },
      error: (error) => {
        this.isDeletingTerminal.set(false);
        this.terminalToDelete.set(null);
        this.deleteDialogOpen.set(false);
        this.setFieldError('terminals', this.terminalErrorMessage(error));
      }
    });
  }

  onInvoicePrefixInput(event: Event): void {
    const element = event.target as HTMLInputElement;
    const upper = element.value.toUpperCase();
    if (upper !== element.value) {
      const caret = element.selectionStart;
      element.value = upper;
      element.setSelectionRange?.(caret, caret);
    }
    this.outletForm.get('invoicePrefix')!.setValue(upper);
    this.clearFieldError('invoicePrefix');
  }

  clearFieldError(field: keyof FieldErrors): void {
    if (this.fieldErrors()[field]) {
      this.fieldErrors.update(errors => ({ ...errors, [field]: undefined }));
    }
  }

  isDuplicateTerminalName(row: TerminalRow): boolean {
    const name = normalizeName(row.group.controls.name.value);
    if (!name) return false;
    return this.terminalRows().filter(candidate => normalizeName(candidate.group.controls.name.value) === name).length > 1;
  }

  terminalNameError(row: TerminalRow): string {
    return this.isDuplicateTerminalName(row) ? 'Another terminal at this outlet already has this name.' : '';
  }

  /** Request body: `terminals` is only sent for users who may manage them, and invoicePrefix only while it is still editable. */
  buildPayload(): CreateOutletRequest {
    const value = this.outletForm.getRawValue();
    const payload: CreateOutletRequest = {
      name: value.name,
      address: value.address,
      contactNumber: value.contactNumber,
      managerId: value.managerId
    };

    const prefix = String(value.invoicePrefix ?? '').trim().toUpperCase();
    if (!this.invoicePrefixLocked() && prefix) {
      payload.invoicePrefix = prefix;
    }

    const timeZone = String(value.timeZoneId ?? '').trim();
    if (this.isEditMode()) {
      // '' clears the outlet override so it inherits the Business default again.
      payload.timeZoneId = timeZone;
    } else if (timeZone) {
      payload.timeZoneId = timeZone;
    }

    if (this.canManageTerminals) {
      payload.terminals = this.terminalRows().map((row): OutletTerminalInput => {
        const terminal = { name: row.group.controls.name.value.trim(), isActive: row.group.controls.isActive.value };
        return row.id === null ? terminal : { id: row.id, ...terminal };
      });
    }
    return payload;
  }

  onSubmit(): void {
    if (this.isSaving()) return;

    if (this.outletForm.invalid) {
      this.outletForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.error.set(null);
    this.fieldErrors.set({});

    const payload = this.buildPayload();
    const warnNoActiveTerminal = this.canManageTerminals && this.activeTerminalCount === 0;
    const request = this.isEditMode()
      ? this.outletService.updateOutlet(this.outletId()!, payload)
      : this.outletService.createOutlet(payload);

    request.subscribe({
      next: () => {
        this.isSaving.set(false);
        if (warnNoActiveTerminal) {
          this.alertService.warning(
            'The outlet was saved without an active terminal. Sales are blocked at this outlet until an active terminal exists.',
            'No active terminal'
          );
        }
        this.location.back();
      },
      error: (error) => {
        console.error('Error saving outlet:', error);
        this.applySaveError(error);
        this.isSaving.set(false);
      }
    });
  }

  /** Maps the API's stable error codes to the field they belong to; anything else stays a form-level message. */
  applySaveError(error: any): void {
    const code = extractApiErrorCode(error);
    const message: string | undefined = error?.message ?? error?.error?.error;

    switch (code) {
      case 'OUTLET_INVOICE_PREFIX_INVALID':
        this.setFieldError('invoicePrefix', 'Use 1–10 letters or digits, starting with a letter.');
        break;
      case 'OUTLET_INVOICE_PREFIX_EXISTS':
        this.setFieldError('invoicePrefix', 'Another outlet in this business already uses this prefix.');
        break;
      case 'OUTLET_INVOICE_PREFIX_LOCKED':
        this.setFieldError('invoicePrefix', 'The prefix cannot change because invoices were already issued for this outlet.');
        this.setInvoicePrefixLocked(true);
        break;
      case 'OUTLET_INVOICE_PREFIX_AMBIGUOUS':
        this.setFieldError(
          'invoicePrefix',
          'This prefix clashes with another outlet: one prefix cannot extend another using only digits (for example DHK and DHK01).'
        );
        break;
      case 'TERMINAL_NAME_EXISTS':
      case 'TERMINAL_INVALID':
      case 'TERMINAL_HAS_OPEN_SESSION':
      case 'TERMINAL_HAS_HISTORY':
        this.setFieldError('terminals', this.terminalErrorMessage(error));
        break;
      case 'forbidden':
        this.error.set('You do not have permission to make this change.');
        break;
      default:
        this.error.set(message || 'Failed to save outlet');
    }
  }

  private terminalErrorMessage(error: any): string {
    switch (extractApiErrorCode(error)) {
      case 'TERMINAL_NAME_EXISTS':
        return 'Another terminal at this outlet already has this name. Terminal names must be unique.';
      case 'TERMINAL_INVALID':
        return `Every terminal needs a name of up to ${TERMINAL_NAME_MAX_LENGTH} characters.`;
      case 'TERMINAL_HAS_OPEN_SESSION':
        return 'A terminal with an open or overdue cash session cannot be deactivated. Close its session first.';
      case 'TERMINAL_HAS_HISTORY':
        return 'This terminal has sales or cash-session history, so it cannot be deleted. Deactivate it instead.';
      default:
        return error?.message || 'The terminal change could not be saved.';
    }
  }

  private setFieldError(field: keyof FieldErrors, message: string): void {
    this.fieldErrors.update(errors => ({ ...errors, [field]: message }));
  }

  cancel(): void {
    this.location.back();
  }
}
