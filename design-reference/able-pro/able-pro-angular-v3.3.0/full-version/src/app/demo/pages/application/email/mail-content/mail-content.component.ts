// angular import
import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { SelectionModel } from '@angular/cdk/collections';
import { MatTableDataSource } from '@angular/material/table';
import { MailData } from 'src/app/fake-data/mail';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

// type
export interface MailDataType {
  images: string;
  name: string;
  text: string;
  symbol: string;
  date: string;
  promo: string;
  forums: string;
}

const ELEMENT_DATA: MailDataType[] = MailData;

@Component({
  selector: 'app-mail-content',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './mail-content.component.html',
  styleUrls: ['./mail-content.component.scss']
})
export class MailContentComponent {
  // public props
  titleContent = true;
  detailsContent = false;
  readonly star = input(false);
  readonly unStar = input(true);
  readonly unImportant = input(true);
  readonly important = input(false);
  readonly paperClip = input(true);
  readonly promotion = input(false);
  readonly forums = input(false);
  readonly common = input(true);

  displayedColumns: string[] = ['select', 'name', 'text', 'symbol', 'date'];
  dataSource = new MatTableDataSource<MailDataType>(ELEMENT_DATA);
  selection = new SelectionModel<MailDataType>(true, []);

  // public method
  isAllSelected() {
    const numSelected = this.selection.selected.length;
    const numRows = this.dataSource.data.length;
    return numSelected === numRows;
  }

  toggleAllRows() {
    if (this.isAllSelected()) {
      this.selection.clear();
      return;
    }

    this.selection.select(...this.dataSource.data);
  }

  checkboxLabel(row?: MailDataType): string {
    if (!row) {
      return `${this.isAllSelected() ? 'deselect' : 'select'} all`;
    }
    return `${this.selection.isSelected(row) ? 'deselect' : 'select'}`;
  }

  detailsContentShow() {
    this.titleContent = !this.titleContent;
    this.detailsContent = !this.detailsContent;
  }

  backToMail() {
    this.detailsContent = false;
    this.titleContent = true;
  }
}
