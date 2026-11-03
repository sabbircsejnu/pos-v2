// angular import
import { AfterViewInit, Component, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';

// angular material
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { studentApply } from 'src/app/fake-data/student_apply';

export interface studentApply {
  name: string;
  img: string;
  department: string;
  qualification: string;
  mobile: string;
  date: string;
  time: string;
}

const ELEMENT_DATA: studentApply[] = studentApply;

@Component({
  selector: 'app-student-apply',
  imports: [SharedModule, CommonModule],
  templateUrl: './student-apply.component.html',
  styleUrl: './student-apply.component.scss'
})
export class StudentApplyComponent implements AfterViewInit {
  // public props
  displayedColumns: string[] = ['name', 'department', 'qualification', 'mobile', 'date', 'action'];
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
}
