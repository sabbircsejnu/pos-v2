// angular import
import { Component, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AddressDialogComponent } from '../invoice-create/address-dialog/address-dialog.component';

// angular material
import { MatDialog } from '@angular/material/dialog';

export interface PeriodicElement {
  no: number;
  name: string;
  description: string;
  qty: number;
  price: number;
}

interface AddressType {
  name?: string;
  address?: string;
  mobile?: string;
  email?: string;
}

const ELEMENT_DATA: PeriodicElement[] = [
  { no: 1, name: 'Apple Series 4 GPS A38 MM Space', description: 'Apple Watch SE Smartwatch', qty: 2, price: 275.0 },
  { no: 2, name: 'Boat On-Ear Wireless', description: 'Mic Bluetooth 4.2, Rockerz 450R', qty: 4, price: 81.99 },
  { no: 3, name: 'Fitbit MX30 Smart Watch', description: '(MX30- waterproof) watch', qty: 5, price: 85.0 }
];

@Component({
  selector: 'app-invoice-edit',
  imports: [SharedModule],
  templateUrl: './invoice-edit.component.html',
  styleUrl: './invoice-edit.component.scss'
})
export class InvoiceEditComponent {
  private dialog = inject(MatDialog);

  // public props
  displayedColumns: string[] = ['no', 'name', 'description', 'qty', 'price', 'amount', 'action'];
  dataSource = ELEMENT_DATA;
  selectedAddress: AddressType | null = null;
  newAddress: AddressType | null = null;

  addressList() {
    const dialogRef = this.dialog.open(AddressDialogComponent, {
      width: '700px'
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.selectedAddress = result;
      }
    });
  }
  addNewAddress() {
    const dialogRef = this.dialog.open(AddressDialogComponent, {
      width: '700px'
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result) {
        this.newAddress = result;
      }
    });
  }
}
