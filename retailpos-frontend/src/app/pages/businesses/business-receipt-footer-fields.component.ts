import { Component, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UI_KIT } from '../../components/shared/ui';

export const POWERED_BY_TEXT_MAX_LENGTH = 150;

/** Platform Super Admin controls for the Business's "Powered by" receipt footer. The owning page decides when to show it. */
@Component({
  selector: 'app-business-receipt-footer-fields',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  template: `
    <app-ui-card title="Receipt footer" subtitle="Platform-controlled. Business users cannot see or change this.">
      <div class="ui-form-stack">
        <label class="ui-check">
          <input appUiControl type="checkbox" name="showPoweredByOnReceipt"
            [ngModel]="show()" [ngModelOptions]="{ standalone: true }" (ngModelChange)="show.set($event)" />
          <span>Show Powered By on Receipt</span>
        </label>

        <app-ui-form-field
          label="Powered By Text"
          [counter]="true"
          [maxLength]="maxLength"
          [hint]="show() && !text().trim() ? 'Nothing prints until text is entered.' : 'For example: Powered by: Irani Mart Technology'">
          <input appUiControl type="text" name="poweredByText" autocomplete="off" [attr.maxlength]="maxLength"
            [ngModel]="text()" [ngModelOptions]="{ standalone: true }" (ngModelChange)="text.set($event)" />
        </app-ui-form-field>

        <ng-content />
      </div>
    </app-ui-card>
  `
})
export class BusinessReceiptFooterFieldsComponent {
  readonly show = model(false);
  readonly text = model('');
  readonly maxLength = POWERED_BY_TEXT_MAX_LENGTH;
}
