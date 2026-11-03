// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { MatDialogRef } from '@angular/material/dialog';

@Component({
  selector: 'app-coupon-dialog',
  imports: [CommonModule, SharedModule],
  templateUrl: './coupon-dialog.component.html',
  styleUrls: ['./coupon-dialog.component.scss']
})
export class CouponDialogComponent {
  dialogRef = inject<MatDialogRef<CouponDialogComponent>>(MatDialogRef);

  selectedValue: string;

  onSelect(value: string): void {
    this.selectedValue = value;
    this.dialogRef.close(this.selectedValue);
  }
}
