// angular import
import { ChangeDetectorRef, Component, inject } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AddressDialogComponent } from './address-dialog/address-dialog.component';

// angular material
import { MatDialog } from '@angular/material/dialog';
import { MatTableDataSource } from '@angular/material/table';

export interface PeriodicElement {
  no: number;
  name: string;
  description: string;
  qty: number;
  price: number;
  amount: number;
}

interface AddressType {
  name?: string;
  address?: string;
  mobile?: string;
  email?: string;
}

@Component({
  selector: 'app-invoice-create',
  imports: [SharedModule],
  templateUrl: './invoice-create.component.html',
  styleUrl: './invoice-create.component.scss'
})
export class InvoiceCreateComponent {
  private dialog = inject(MatDialog);
  private cdr = inject(ChangeDetectorRef);

  elements: PeriodicElement[] = [{ no: 1, name: '', description: '', qty: 0, price: 0, amount: 0 }];

  // public props
  displayedColumns: string[] = ['no', 'name', 'description', 'qty', 'price', 'amount', 'action'];
  dataSource = new MatTableDataSource<PeriodicElement>(this.elements);
  selectedAddress: AddressType | null = null;
  newAddress: AddressType | null = null;
  discountPercent = 0;
  taxPercent = 0;

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
  onInputChange(index: number, field: 'qty' | 'price', value: number): void {
    const item = this.elements[index];
    if (!item) return;

    item[field] = value;
    item.amount = item.qty * item.price;

    this.dataSource.data = [...this.elements]; // Refresh table
  }

  get subTotal(): number {
    return this.elements.reduce((sum, item) => sum + item.amount, 0);
  }

  get discountAmount(): number {
    return (this.subTotal * this.discountPercent) / 100;
  }

  get taxAmount(): number {
    return ((this.subTotal - this.discountAmount) * this.taxPercent) / 100;
  }

  get grandTotal(): number {
    return this.subTotal - this.discountAmount + this.taxAmount;
  }

  deleteItem(no: number): void {
    this.elements = this.elements.filter((item) => item.no !== no);
    this.dataSource.data = [...this.elements];
  }

  addNewItem(): void {
    const newItem = {
      no: this.elements.length > 0 ? Math.max(...this.elements.map((e) => e.no)) + 1 : 1,
      name: '',
      description: '',
      qty: 0,
      price: 0,
      amount: 0
    };
    this.elements.push(newItem);
    this.dataSource.data = [...this.elements];
  }
}
