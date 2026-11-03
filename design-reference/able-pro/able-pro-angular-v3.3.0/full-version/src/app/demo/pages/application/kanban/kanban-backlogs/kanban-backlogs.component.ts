import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { kanbanData } from 'src/app/fake-data/kanban_data';
import { collapseData } from 'src/app/fake-data/kanban_collapse_data';
import { SharedModule } from 'src/app/demo/shared/shared.module';

export interface PeriodicElement {
  title: string;
  position: number;
  status: string;
  assigned: string;
  id: string;
  edit: string;
  priority: string;
  date: string;
}

const ELEMENT_DATA: PeriodicElement[] = kanbanData;

export interface CollapseElement {
  title: string;
  position: number;
  state: string;
  assigned: string;
  id: string;
  edit: string;
  priority: string;
  date: string;
}

const collapse_Data: CollapseElement[] = collapseData;

@Component({
  selector: 'app-kanban-backlogs',
  imports: [CommonModule, SharedModule],
  templateUrl: './kanban-backlogs.component.html',
  styleUrls: ['./kanban-backlogs.component.scss']
})
export class KanbanBacklogsComponent {
  // public props
  dataSource = ELEMENT_DATA;
  dataSource1 = collapse_Data;
  columnsToDisplay = ['position', 'id', 'title', 'edit', 'status', 'assigned', 'priority', 'date'];
  columnsToDisplayWithExpand = [...this.columnsToDisplay];
  expandedElement: PeriodicElement | null;
  displayedColumns: string[] = ['position', 'id', 'title', 'edit', 'status', 'assigned', 'priority', 'date'];
  collapseColumns: string[] = ['position', 'id', 'title', 'edit', 'state', 'assigned', 'priority', 'date'];
}
