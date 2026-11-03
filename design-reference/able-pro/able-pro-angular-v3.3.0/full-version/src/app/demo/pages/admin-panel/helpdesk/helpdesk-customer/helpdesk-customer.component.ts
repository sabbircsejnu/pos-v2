// angular import
import { AfterViewInit, Component, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';

// angular material
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { customerAdd } from 'src/app/fake-data/new-customer-add';
import { AddCustomerComponent } from './add-customer/add-customer.component';
import { MatDialog } from '@angular/material/dialog';

export interface customerAdd {
  name: string;
  email: string;
  account: string;
  login: string;
}

const ELEMENT_DATA: customerAdd[] = customerAdd;

@Component({
  selector: 'app-helpdesk-customer',
  imports: [SharedModule, CommonModule],
  templateUrl: './helpdesk-customer.component.html',
  styleUrl: './helpdesk-customer.component.scss'
})
export class HelpdeskCustomerComponent implements AfterViewInit {
  dialog = inject(MatDialog);

  // public props
  displayedColumns: string[] = ['name', 'email', 'account', 'login', 'action'];
  dataSource = new MatTableDataSource(ELEMENT_DATA);

  // paginator
  readonly paginator = viewChild(MatPaginator);
  readonly sort = viewChild(MatSort);

  // table search filter
  applyFilter(event: Event) {
    const filterValue = (event.target as HTMLInputElement).value;
    this.dataSource.filter = filterValue.trim().toLowerCase();

    if (this.dataSource.paginator) {
      this.dataSource.paginator.firstPage();
    }
  }

  // life cycle event
  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator()!;
    this.dataSource.sort = this.sort()!;
  }

  customerAdd() {
    this.dialog.open(AddCustomerComponent, {
      width: '500px'
    });
  }
}
