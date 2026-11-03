// angular import
import { AfterViewInit, Component, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableDataSource } from '@angular/material/table';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { customerList } from 'src/app/fake-data/customer_list';
import { CustomerDetailsComponent } from './customer-details/customer-details.component';
import { CustomerDetailsEditComponent } from './customer-details-edit/customer-details-edit.component';

export interface PeriodicElement {
  id: number;
  name: string;
  img: string;
  email: string;
  contact: string;
  order: number;
  spent: string;
  status: string;
  status_type: string;
}

const ELEMENT_DATA: PeriodicElement[] = customerList;

@Component({
  selector: 'app-customer-list',
  imports: [CommonModule, SharedModule],
  templateUrl: './customer-list.component.html',
  styleUrls: ['./customer-list.component.scss']
})
export class CustomerListComponent implements AfterViewInit {
  dialog = inject(MatDialog);

  // public props
  displayedColumns: string[] = ['id', 'name', 'contact', 'order', 'spent', 'status', 'action'];
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

  // open dialog customer details view
  customerDetails(): void {
    this.dialog.open(CustomerDetailsComponent, {
      width: '800px'
    });
  }

  // open dialog of customer edit details
  customerDetailsEdit() {
    this.dialog.open(CustomerDetailsEditComponent, {
      width: '800px'
    });
  }

  // life cycle event
  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator()!;
    this.dataSource.sort = this.sort()!;
  }
}
