// project import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

export interface DialogData {
  imagePath: string;
}

@Component({
  selector: 'app-modal-dialog',
  templateUrl: 'modal-dialog.component.html',
  styleUrls: ['modal-dialog.component.scss'],
  imports: [SharedModule, CommonModule]
})
export class DialogDataExampleDialogComponent {
  data = inject<DialogData>(MAT_DIALOG_DATA);
}
