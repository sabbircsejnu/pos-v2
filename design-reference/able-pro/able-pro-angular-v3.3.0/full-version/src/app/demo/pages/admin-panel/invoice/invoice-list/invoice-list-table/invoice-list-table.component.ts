// angular import
import { AfterViewInit, Component, viewChild } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { invoiceListTableData } from 'src/app/fake-data/invoice-list-table-data';

export interface PeriodicElement {
  id: number;
  name: string;
  image: string;
  email: string;
  create_date: string;
  due_date: string;
  qty: number;
  status: string;
}

const ELEMENT_DATA: PeriodicElement[] = invoiceListTableData;

@Component({
  selector: 'app-invoice-list-table',
  imports: [SharedModule],
  templateUrl: './invoice-list-table.component.html',
  styleUrl: './invoice-list-table.component.scss'
})
export class InvoiceListTableComponent implements AfterViewInit {
  // public props
  displayedColumns: string[] = ['id', 'name', 'create_date', 'due_date', 'qty', 'status', 'action'];
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

  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator()!;
    this.dataSource.sort = this.sort()!;
  }
}
