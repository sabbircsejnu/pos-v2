// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatSnackBarRef } from '@angular/material/snack-bar';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-pizza-party',
  imports: [CommonModule, SharedModule],
  templateUrl: './pizza-party.component.html',
  styleUrls: ['./pizza-party.component.scss']
})
export class PizzaPartyComponent {
  // public props
  snackBarRef = inject(MatSnackBarRef);
}
