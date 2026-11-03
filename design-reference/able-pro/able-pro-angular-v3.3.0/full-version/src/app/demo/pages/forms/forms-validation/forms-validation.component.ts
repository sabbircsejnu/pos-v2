// angular import
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { NarikCustomValidatorsModule } from '@narik/custom-validators';

export class FormInput {
  email!: string;
  password!: string;
  confirmPassword!: string;
  requiredInput!: string;
  url!: string;
  phone!: string;
  cmbGear!: string;
  address!: string;
  file!: string;
  switcher!: string;
}

@Component({
  selector: 'app-forms-validation',
  imports: [CommonModule, SharedModule, NarikCustomValidatorsModule],
  templateUrl: './forms-validation.component.html',
  styleUrls: ['./forms-validation.component.scss']
})
export class FormsValidationComponent implements OnInit {
  // private props
  formInput!: FormInput;
  form!: string;
  isSubmit: boolean;

  // Constructor
  constructor() {
    this.isSubmit = false;
  }

  // Life cycle events
  ngOnInit() {
    this.formInput = {
      email: '',
      password: '',
      confirmPassword: '',
      requiredInput: '',
      url: '',
      phone: '',
      cmbGear: '',
      address: '',
      file: '',
      switcher: ''
    };
  }

  // private method
  save(form: { valid: undefined }) {
    if (!form.valid) {
      this.isSubmit = true;
      return;
    }
  }
}
