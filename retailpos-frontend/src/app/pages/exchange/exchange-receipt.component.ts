import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ExchangeReceiptDocument, ExchangeReceiptLine, exchangeMethodLabel, exchangeReasonLabel } from '../../models/exchange.model';

/**
 * The 80 mm Exchange Receipt, styled like the thermal invoice. It prints only the server's immutable receipt snapshot:
 * no internal note and no loyalty points (D-05). Text is rendered through interpolation only.
 */
@Component({
  selector: 'app-exchange-receipt',
  standalone: true,
  styleUrls: ['../pos/thermal-receipt.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let receipt = data();
    @let rd = receipt.document;
    <div class="thermal-receipt">
      <header class="tr-center">
        <h2 class="tr-business">{{ receipt.businessName || 'Business' }}</h2>
        @if (receipt.outletName) { <p class="tr-line">Branch: {{ receipt.outletName }}</p> }
        <h3 class="tr-title">Exchange Receipt</h3>
      </header>

      <dl class="tr-meta tr-section">
        <dt>Exchange No</dt><dd>{{ rd.exchangeNumber }}</dd>
        <dt>Date</dt><dd>{{ rd.businessDate }}</dd>
        <dt>Cashier</dt><dd>{{ receipt.cashierName || '—' }}</dd>
        @if (receipt.salespersonName) { <dt>Salesperson</dt><dd>{{ receipt.salespersonName }}</dd> }
        <dt>Original Inv.</dt><dd>{{ rd.originalInvoiceNumbers.join(', ') }}</dd>
        <dt>Reason</dt><dd>{{ reason() }}</dd>
      </dl>

      <section class="tr-section">
        <b>Returned items</b>
        <table class="tr-items">
          <thead>
            <tr><th scope="col">Item</th><th scope="col" class="tr-qty">Qty</th><th scope="col" class="tr-num tr-total">Credit</th></tr>
            <tr><td class="tr-rule" colspan="3"></td></tr>
          </thead>
          <tbody>
            @for (line of rd.returned; track $index) {
              <tr>
                <td>{{ name(line) }}@if (line.invoiceNumber) { <br />Inv {{ line.invoiceNumber }} }</td>
                <td class="tr-qty tr-qty-value">{{ quantity(line) }}</td>
                <td class="tr-num">{{ money(line.gross) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      <section class="tr-section">
        <b>Replacement items</b>
        <table class="tr-items">
          <thead>
            <tr><th scope="col">Item</th><th scope="col" class="tr-qty">Qty</th><th scope="col" class="tr-num tr-total">Amount</th></tr>
            <tr><td class="tr-rule" colspan="3"></td></tr>
          </thead>
          <tbody>
            @for (line of rd.replacements; track $index) {
              <tr>
                <td>{{ name(line) }}</td>
                <td class="tr-qty tr-qty-value">{{ quantity(line) }}</td>
                <td class="tr-num">{{ money(line.gross) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      <section class="tr-section tr-amounts">
        <div><span>Returned credit</span><b>{{ money(rd.returnGross) }}</b></div>
        <div><span>Return VAT (incl.)</span><b>{{ money(rd.returnVat) }}</b></div>
        <div><span>Replacement value</span><b>{{ money(rd.replacementGross) }}</b></div>
        @if (rd.replacementDiscount > 0) { <div><span>Replacement discount</span><b>{{ money(rd.replacementDiscount) }}</b></div> }
        <div><span>Replacement VAT (incl.)</span><b>{{ money(rd.replacementVat) }}</b></div>
      </section>

      <section class="tr-section tr-amounts">
        <div><b>Difference</b><b>{{ signed(rd.netAdjustmentGross) }}</b></div>
        <div><span>{{ settlementTitle() }}</span><b>{{ money(rd.settlementAmount) }}</b></div>
        @for (settlement of rd.settlements; track $index) {
          <div><span>{{ methodLabel(settlement.method) }}@if (settlement.referenceLastFour) { (ref ****{{ settlement.referenceLastFour }}) }</span><b>{{ money(settlement.amount) }}</b></div>
          @if (settlement.tendered) { <div><span>Tendered</span><b>{{ money(settlement.tendered) }}</b></div> }
          @if (settlement.changeGiven) { <div><span>Change</span><b>{{ money(settlement.changeGiven) }}</b></div> }
        }
      </section>

      <footer class="tr-section tr-center">
        <p class="tr-line">Original invoice numbers are kept as references.</p>
      </footer>
    </div>
  `
})
export class ExchangeReceiptComponent {
  readonly data = input.required<ExchangeReceiptDocument>();

  readonly reason = computed(() => {
    const rd = this.data().document;
    const label = exchangeReasonLabel(rd.reasonCode);
    return rd.reasonCode === 'other' && rd.reasonOtherText ? `${label}: ${rd.reasonOtherText}` : label;
  });

  readonly settlementTitle = computed(() => {
    switch (this.data().document.settlementDirection) {
      case 'collect': return 'Collected';
      case 'refund': return 'Refunded';
      default: return 'Settled (no money moved)';
    }
  });

  name(line: ExchangeReceiptLine): string {
    return this.data().variantNames?.[String(line.variantId)] ?? `Item ${line.variantId}`;
  }

  quantity(line: ExchangeReceiptLine): string {
    if (line.enteredQuantity !== null && line.enteredQuantity !== undefined && line.unitLabel) {
      return `${line.enteredQuantity} ${line.unitLabel}`;
    }
    return String(line.quantity);
  }

  methodLabel(method: string): string {
    return exchangeMethodLabel(method);
  }

  money(value: number | null | undefined): string {
    return Number(value ?? 0).toFixed(2);
  }

  signed(value: number | null | undefined): string {
    const number = Number(value ?? 0);
    return `${number > 0 ? '+' : ''}${number.toFixed(2)}`;
  }
}
