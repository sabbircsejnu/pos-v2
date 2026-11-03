// angular import
import { AfterViewInit, Component, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// angular material
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { teacherList } from 'src/app/fake-data/teacher_list';

export interface teacherList {
  name: string;
  img: string;
  department: string;
  qualification: string;
  mobile: string;
  date: string;
}

const ELEMENT_DATA: teacherList[] = teacherList;

@Component({
  selector: 'app-teacher-list',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './teacher-list.component.html',
  styleUrl: './teacher-list.component.scss'
})
export class TeacherListComponent implements AfterViewInit {
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
