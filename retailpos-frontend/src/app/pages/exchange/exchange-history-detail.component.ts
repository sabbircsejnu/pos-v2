import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { UI_KIT } from '../../components/shared/ui';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import {
  ExchangeDetail,
  ExchangeReceiptDocument,
  ExchangeReversalEligibility,
  exchangeApprovalLabel,
  exchangeMethodLabel,
  exchangeReasonLabel,
  settlementDirectionLabel
} from '../../models/exchange.model';
import { AuthService } from '../../services/auth.service';
import { ConnectivityService } from '../../services/connectivity.service';
import { ExchangeService } from '../../services/exchange.service';
import { PosService, PosTerminalDto } from '../../services/pos.service';
import { newIdempotencyKey } from '../../services/uom.service';
import { resolveTerminalSelection } from '../pos/pos-terminals';
import { applyReceiptPrintPage } from '../pos/receipt-print-page';
import { readExchangeError } from './exchange-errors';
import { ExchangeReceiptComponent } from './exchange-receipt.component';
import { SettlementDraft, buildSettlements, defaultSettlement, settlementError } from './exchange-settlement';
import { ExchangeSettlementPanelComponent } from './exchange-settlement-panel.component';
import { downloadBlob } from './exchange-workflow.component';

/**
 * Exchange details (FR-EX-151): source invoices and lines, replacements, settlements with masked references,
 * approvals (no credentials), reversal link, receipt reprint/PDF and the controlled reversal (FR-EX-160..165).
 */
@Component({
  selector: 'app-exchange-history-detail',
  standalone: true,
  imports: [FormsModule, RouterLink, AppCurrencyPipe, ExchangeReceiptComponent, ExchangeSettlementPanelComponent, ...UI_KIT],
  styleUrls: ['./exchange.css'],
  templateUrl: './exchange-history-detail.component.html'
})
export class ExchangeHistoryDetailComponent implements OnInit {
  private readonly exchanges = inject(ExchangeService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly pos = inject(PosService);
  private readonly destroyRef = inject(DestroyRef);
  readonly online = inject(ConnectivityService).online;

  readonly reasonLabel = exchangeReasonLabel;
  readonly approvalLabel = exchangeApprovalLabel;
  readonly methodLabel = exchangeMethodLabel;
  readonly directionLabel = settlementDirectionLabel;

  readonly exchangeId = signal(0);
  readonly detail = signal<ExchangeDetail | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly breadcrumbs = computed(() => [
    { label: 'Exchanges', link: '/exchanges' },
    { label: this.detail()?.exchangeNumber ?? 'Exchange' }
  ]);

  readonly receipt = signal<ExchangeReceiptDocument | null>(null);
  readonly receiptBusy = signal(false);
  readonly receiptError = signal('');

  readonly canReverse = computed(() => this.auth.hasPermission('pos.exchange.reverse')
    && (this.detail()?.status ?? '').toLowerCase() === 'completed' && !this.detail()?.reversal);
  readonly reverseOpen = signal(false);
  readonly eligibility = signal<ExchangeReversalEligibility | null>(null);
  readonly eligibilityLoading = signal(false);
  readonly reverseReason = signal('');
  readonly reverseSettlement = signal<SettlementDraft>(defaultSettlement('none', 0));
  readonly reverseBusy = signal(false);
  readonly reverseError = signal('');
  private reverseAttempt: { signature: string; key: string } | null = null;
  readonly terminals = signal<PosTerminalDto[]>([]);
  readonly terminalId = signal<number | null>(null);

  readonly reverseProblem = computed(() => {
    const eligibility = this.eligibility();
    if (!eligibility) return 'Checking whether this exchange can be reversed…';
    if (!eligibility.canReverse) return 'This exchange cannot be reversed right now.';
    if (!this.reverseReason().trim()) return 'Enter the reversal reason.';
    return settlementError(eligibility.settlementDirection, eligibility.settlementAmount, this.reverseSettlement());
  });

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.exchangeId.set(Number.isFinite(id) ? id : 0);
    this.load();
  }

  load(): void {
    const id = this.exchangeId();
    if (!id) {
      this.loadError.set('Exchange not found.');
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);
    this.exchanges.getById(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.loading.set(false);
        this.detail.set(res?.data ?? null);
        if (!res?.data) this.loadError.set('Exchange not found.');
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(readExchangeError(err).message);
      }
    });
  }

  /** Reprints use the immutable completion snapshot; the reprint is audited by the server. */
  reprint(): void {
    const id = this.exchangeId();
    if (this.receiptBusy()) return;
    this.receiptBusy.set(true);
    this.receiptError.set('');
    this.exchanges.recordReprint(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.exchanges.getReceiptDocument(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.receiptBusy.set(false);
        const doc = res?.data ?? null;
        this.receipt.set(doc);
        if (!doc) return;
        // Let the receipt render before measuring the roll height and printing.
        setTimeout(() => {
          applyReceiptPrintPage('80mm');
          window.print();
        }, 0);
      },
      error: (err: unknown) => {
        this.receiptBusy.set(false);
        this.receiptError.set(readExchangeError(err).message);
      }
    });
  }

  downloadPdf(mode: 'thermal' | 'a4'): void {
    const detail = this.detail();
    if (!detail || this.receiptBusy()) return;
    this.receiptBusy.set(true);
    this.exchanges.getReceiptPdf(detail.id, mode).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        this.receiptBusy.set(false);
        downloadBlob(blob, `exchange-${detail.exchangeNumber}${mode === 'a4' ? '-a4' : ''}.pdf`);
      },
      error: (err: unknown) => {
        this.receiptBusy.set(false);
        this.receiptError.set(readExchangeError(err).message);
      }
    });
  }

  openReverse(): void {
    if (!this.canReverse()) return;
    this.reverseOpen.set(true);
    this.reverseError.set('');
    this.reverseReason.set('');
    this.eligibility.set(null);
    this.eligibilityLoading.set(true);
    this.loadTerminals();
    this.exchanges.getReversalEligibility(this.exchangeId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.eligibilityLoading.set(false);
        const eligibility = res?.data ?? null;
        this.eligibility.set(eligibility);
        if (eligibility) this.reverseSettlement.set(defaultSettlement(eligibility.settlementDirection, eligibility.settlementAmount));
      },
      error: (err: unknown) => {
        this.eligibilityLoading.set(false);
        this.reverseError.set(readExchangeError(err).message);
      }
    });
  }

  submitReverse(): void {
    const eligibility = this.eligibility();
    if (this.reverseBusy() || !eligibility || this.reverseProblem() || !this.online()) return;
    const body = {
      reason: this.reverseReason().trim(),
      terminalId: this.terminalId(),
      settlements: buildSettlements(eligibility.settlementDirection, eligibility.settlementAmount, this.reverseSettlement())
    };
    const signature = JSON.stringify(body);
    if (!this.reverseAttempt || this.reverseAttempt.signature !== signature) {
      this.reverseAttempt = { signature, key: newIdempotencyKey('exchange-reversal') };
    }
    this.reverseBusy.set(true);
    this.reverseError.set('');
    this.exchanges.reverse(this.exchangeId(), { ...body, idempotencyKey: this.reverseAttempt.key })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.reverseBusy.set(false);
          this.reverseAttempt = null;
          this.reverseOpen.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.reverseBusy.set(false);
          this.reverseError.set(readExchangeError(err).message);
        }
      });
  }

  /** The reversal posts to the cashier's active session on this outlet's terminal. */
  private loadTerminals(): void {
    const outletId = this.auth.getUserValue()?.outletId;
    if (!outletId || this.terminals().length > 0) return;
    this.pos.getTerminals(outletId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res: { data?: PosTerminalDto[] } | null) => {
        const list = res?.data ?? [];
        this.terminals.set(list);
        this.terminalId.set(resolveTerminalSelection(list, this.terminalId()).selectedId);
      },
      error: () => this.terminals.set([])
    });
  }

  onTerminalChange(value: unknown): void {
    const id = Number(value);
    this.terminalId.set(Number.isFinite(id) && id > 0 ? id : null);
  }

  signed(value: number): string {
    return value > 0 ? '+' : '';
  }
}
