// Angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroupDirective, NgForm, Validators } from '@angular/forms';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ErrorStateMatcher } from '@angular/material/core';

interface Food {
  value: string;
  viewValue: string;
}

interface Car {
  value: string;
  viewValue: string;
}

export class MyErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: FormControl | null, form: FormGroupDirective | NgForm | null): boolean {
    const isSubmitted = form && form.submitted;
    return !!(control && control.invalid && (control.dirty || control.touched || isSubmitted));
  }
}

@Component({
  selector: 'app-select',
  imports: [CommonModule, SharedModule],
  templateUrl: './select.component.html',
  styleUrls: ['./select.component.scss']
})
export class SelectComponent {
  // public props
  toppings = new FormControl('');
  disableSelect = new FormControl(false);
  selectedValue: string;
  selectedCar: string;
  selected1 = 'option2';
  toppingList: string[] = ['Extra cheese', 'Mushroom', 'Onion', 'Pepperoni', 'Sausage', 'Tomato'];
  selected = new FormControl('valid', [Validators.required, Validators.pattern('valid')]);
  selectFormControl = new FormControl('valid', [Validators.required, Validators.pattern('valid')]);
  nativeSelectFormControl = new FormControl('valid', [Validators.required, Validators.pattern('valid')]);
  matcher = new MyErrorStateMatcher();

  // public method
  foods: Food[] = [
    { value: 'steak-0', viewValue: 'Steak' },
    { value: 'pizza-1', viewValue: 'Pizza' },
    { value: 'tacos-2', viewValue: 'Tacos' }
  ];

  cars: Car[] = [
    { value: 'volvo', viewValue: 'Volvo' },
    { value: 'saab', viewValue: 'Saab' },
    { value: 'mercedes', viewValue: 'Mercedes' }
  ];
}
