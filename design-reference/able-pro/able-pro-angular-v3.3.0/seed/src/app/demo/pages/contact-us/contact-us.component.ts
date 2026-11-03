// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, Validators } from '@angular/forms';
import { BreakpointObserver } from '@angular/cdk/layout';

// project import
import { SharedModule } from '../../shared/shared.module';

@Component({
  selector: 'app-contact-us',
  imports: [CommonModule, SharedModule],
  templateUrl: './contact-us.component.html',
  styleUrls: ['./contact-us.component.scss']
})
export class ContactUsComponent {
  // public props
  breakpoint = 12;
  isMobile = false;
  selectedOption = '1';
  email = new FormControl('', [Validators.required, Validators.email]);
  mobileNumberFormControl = new FormControl('', [Validators.minLength(10)]);
  breakpointObserver = inject(BreakpointObserver);

  // constructor
  constructor() {
    this.breakpointObserver.observe(['(max-width: 767px)']).subscribe((result) => {
      this.isMobile = result.matches;
    });
  }

  // public method
  getErrorMessage() {
    if (this.email.hasError('required')) {
      return 'You must enter a value';
    }

    return this.email.hasError('email') ? 'Not a valid email' : '';
  }
}
