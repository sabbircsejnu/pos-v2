// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormControl, Validators } from '@angular/forms';
import { FloatLabelType } from '@angular/material/form-field';
import { ThemePalette } from '@angular/material/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-form-field',
  imports: [CommonModule, SharedModule],
  templateUrl: './form-field.component.html',
  styleUrls: ['./form-field.component.scss']
})
export class FormFieldComponent {
  private _formBuilder = inject(FormBuilder);

  // public props
  // password input show nad hide
  hide = true;
  // theme color select
  colorControl = new FormControl('primary' as ThemePalette);
  // validation of email input
  email = new FormControl('', [Validators.required, Validators.email]);
  // form files with label
  hideRequiredControl = new FormControl(false);
  floatLabelControl = new FormControl('auto' as FloatLabelType);
  options = this._formBuilder.group({
    hideRequired: this.hideRequiredControl,
    floatLabel: this.floatLabelControl
  });

  // public method
  getErrorMessage() {
    if (this.email.hasError('required')) {
      return 'You must enter a value';
    }
    return this.email.hasError('email') ? 'Not a valid email' : '';
  }

  getFloatLabelValue(): FloatLabelType {
    return this.floatLabelControl.value || 'auto';
  }
}
