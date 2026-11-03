// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { FormBuilder, Validators } from '@angular/forms';

@Component({
  selector: 'app-stepper',
  imports: [CommonModule, SharedModule],
  templateUrl: './stepper.component.html',
  styleUrls: ['./stepper.component.scss']
})
export class StepperComponent {
  private _formBuilder = inject(FormBuilder);

  // public props
  firstFormGroup = this._formBuilder.group({
    firstCtrl: ['', Validators.required]
  });
  secondFormGroup = this._formBuilder.group({
    secondCtrl: ['', Validators.required]
  });
  isLinear = false;
  isEditable = false;
}
