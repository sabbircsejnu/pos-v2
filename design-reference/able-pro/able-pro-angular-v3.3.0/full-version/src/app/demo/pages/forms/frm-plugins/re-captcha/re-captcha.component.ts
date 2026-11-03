// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { ScCheckboxReCaptcha } from '@semantic-components/re-captcha';

export interface FormModel {
  captcha?: string;
}

@Component({
  selector: 'app-re-captcha',
  imports: [CommonModule, SharedModule, ScCheckboxReCaptcha, ReactiveFormsModule],
  templateUrl: './re-captcha.component.html',
  styleUrls: ['./re-captcha.component.scss']
})
export class ReCaptchaComponent {
  siteKey = '6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI'; //You can also provide it globally with provideScReCaptchaSettings

  checkboxReCaptchaForm = new FormGroup({
    captcha: new FormControl('')
  });

  onSubmit() {
    console.log('User Successfully Verified');
  }
}
