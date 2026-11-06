import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ReceiptDocumentDto } from '../../models/sale.model';

/**
 * The 80 mm thermal invoice. Everything printed comes from the server's receipt document; user-configured
 * text is only rendered through interpolation, so it can never become markup.
 */
@Component({
  selector: 'app-thermal-receipt',
  standalone: true,
  templateUrl: './thermal-receipt.component.html',
  styleUrls: ['./thermal-receipt.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ThermalReceiptComponent {
  readonly document = input.required<ReceiptDocumentDto>();

  money(value: number | null | undefined): string {
    return Number(value ?? 0).toFixed(2);
  }
}
