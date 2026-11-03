// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroupDirective, NgForm, Validators } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

/** Error when invalid control is dirty, touched, or submitted. */
export class MyErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: FormControl | null, form: FormGroupDirective | NgForm | null): boolean {
    const isSubmitted = form && form.submitted;
    return !!(control && control.invalid && (control.dirty || control.touched || isSubmitted));
  }
}

@Component({
  selector: 'app-input-compo',
  imports: [CommonModule, SharedModule],
  templateUrl: './input-compo.component.html',
  styleUrls: ['./input-compo.component.scss']
})
export class InputCompoComponent {
  // public props
  value = 'Clear me';
  // email validation
  emailFormControl = new FormControl('', [Validators.required, Validators.email]);
  matcher = new MyErrorStateMatcher();
}
