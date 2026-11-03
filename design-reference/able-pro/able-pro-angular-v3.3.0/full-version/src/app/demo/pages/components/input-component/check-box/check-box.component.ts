// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ThemePalette } from '@angular/material/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { FormBuilder } from '@angular/forms';

export interface Task {
  name: string;
  completed: boolean;
  color: ThemePalette;
  subtasks?: Task[];
}

@Component({
  selector: 'app-check-box',
  imports: [CommonModule, SharedModule],
  templateUrl: './check-box.component.html',
  styleUrls: ['./check-box.component.scss']
})
export class CheckBoxComponent {
  private _formBuilder = inject(FormBuilder);

  // public props
  checked = false;
  indeterminate = false;
  labelPosition: 'before' | 'after' = 'after';
  disabled = false;
  allComplete = false;
  task: Task = {
    name: 'Indeterminate',
    completed: false,
    color: 'primary',
    subtasks: [
      { name: 'Primary', completed: false, color: 'primary' },
      { name: 'Accent', completed: false, color: 'accent' },
      { name: 'Warn', completed: false, color: 'warn' }
    ]
  };

  // public method
  updateAllComplete() {
    this.allComplete = this.task.subtasks != null && this.task.subtasks.every((t) => t.completed);
  }

  someComplete(): boolean {
    if (this.task.subtasks == null) {
      return false;
    }
    return this.task.subtasks.filter((t) => t.completed).length > 0 && !this.allComplete;
  }

  setAll(completed: boolean) {
    this.allComplete = completed;
    if (this.task.subtasks == null) {
      return;
    }
    this.task.subtasks.forEach((t) => (t.completed = completed));
  }

  toppings = this._formBuilder.group({
    pepperoni: false,
    extraCheese: false,
    mushroom: false
  });
}
