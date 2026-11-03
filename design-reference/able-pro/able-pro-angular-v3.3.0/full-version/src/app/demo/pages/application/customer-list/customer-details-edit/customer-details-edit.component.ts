// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogRef } from '@angular/material/dialog';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

@Component({
  selector: 'app-customer-details-edit',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './customer-details-edit.component.html',
  styleUrls: ['./customer-details-edit.component.scss']
})
export class CustomerDetailsEditComponent {
  // public props
  dialogRef = inject<MatDialogRef<CustomerDetailsEditComponent>>(MatDialogRef);
}
