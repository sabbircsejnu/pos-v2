// angular import
import { Component, ElementRef, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipInputEvent } from '@angular/material/chips';
import { FormControl } from '@angular/forms';
import { Observable, startWith, map } from 'rxjs';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

export interface Vegetable {
  name: string;
}

export interface Fruit {
  name: string;
}

@Component({
  selector: 'app-chips',
  imports: [CommonModule, SharedModule],
  templateUrl: './chips.component.html',
  styleUrls: ['./chips.component.scss']
})
export class ChipsComponent {
  // public props
  fruitInput = viewChild<ElementRef<HTMLInputElement>>('fruitInput');

  // autoComplete
  fruitCtrl = new FormControl('');
  filteredFruits: Observable<string[]>;
  fruits: string[] = ['Lemon'];
  allFruits: string[] = ['Apple', 'Lemon', 'Lime', 'Orange', 'Strawberry'];

  // Drag And Drop
  vegetables: Vegetable[] = [
    { name: 'apple' },
    { name: 'banana' },
    { name: 'strawberry' },
    { name: 'orange' },
    { name: 'kiwi' },
    { name: 'cherry' }
  ];

  drop(event: CdkDragDrop<Vegetable[]>) {
    moveItemInArray(this.vegetables, event.previousIndex, event.currentIndex);
  }

  // constructor
  constructor() {
    this.filteredFruits = this.fruitCtrl.valueChanges.pipe(
      startWith(null),
      map((fruit: string | null) => (fruit ? this._filter(fruit) : this.allFruits.slice()))
    );
  }

  // public method
  add(event: MatChipInputEvent): void {
    const value = (event.value || '').trim();

    // Add our fruit
    if (value) {
      this.fruits.push(value);
    }

    // Clear the input value
    event.chipInput.clear();

    this.fruitCtrl.setValue(null);
  }

  remove(fruit: string): void {
    const index = this.fruits.indexOf(fruit);

    if (index >= 0) {
      this.fruits.splice(index, 1);
    }
  }

  selected(event: MatAutocompleteSelectedEvent): void {
    this.fruits.push(event.option.viewValue);
    this.fruitInput()!.nativeElement.value = '';
    this.fruitCtrl.setValue(null);
  }

  private _filter(value: string): string[] {
    const filterValue = value.toLowerCase();

    return this.allFruits.filter((fruit) => fruit.toLowerCase().includes(filterValue));
  }
}
