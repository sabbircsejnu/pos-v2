// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';
import { SelectionModel } from '@angular/cdk/collections';
import { MatTableDataSource } from '@angular/material/table';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { FileSliderComponent } from './file-slider/file-slider.component';
import { fileData } from 'src/app/fake-data/file_data';
import { FileAddComponent } from './file-add/file-add.component';
import { FileShareComponent } from './file-share/file-share.component';
import { FileManagerLayoutService } from './file-manager-layout.service';

export interface PeriodicElement {
  title: string;
  images: string;
  size: string;
  date: string;
  user: number;
  rating: string;
}

const ELEMENT_DATA: PeriodicElement[] = fileData;

@Component({
  selector: 'app-file-manager',
  imports: [CommonModule, SharedModule, FileSliderComponent],
  templateUrl: './file-manager.component.html',
  styleUrls: ['./file-manager.component.scss']
})
export class FileManagerComponent {
  private layoutService = inject(FileManagerLayoutService);
  dialog = inject(MatDialog);

  // public props
  selectedTabIndex = 0;
  fileEdit!: boolean;

  displayedColumns: string[] = ['select', 'name', 'size', 'date', 'user', 'options'];
  dataSource = new MatTableDataSource<PeriodicElement>(ELEMENT_DATA);
  selection = new SelectionModel<PeriodicElement>(true, []);

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

  checkboxLabel(row?: PeriodicElement): string {
    if (!row) {
      return `${this.isAllSelected() ? 'deselect' : 'select'} all`;
    }
    return `${this.selection.isSelected(row) ? 'deselect' : 'select'}`;
  }

  fileSlider() {
    this.layoutService.toggleFileSide();
  }

  tabChanged(index: number) {
    this.selectedTabIndex = index;
  }

  newFileAdd(): void {
    this.dialog.open(FileAddComponent, {
      width: '500px'
    });
  }
  fileShare(): void {
    this.dialog.open(FileShareComponent, {
      width: '500px'
    });
  }

  filter_data = [
    {
      background: ' bg-primary-50',
      color: 'text-primary-500',
      icon: '#custom-note-1',
      type: 'Documents',
      store: '15 GB',
      store_bg: 'bg-primary-500'
    },
    {
      background: ' bg-warn-50',
      color: 'text-warn-500',
      icon: '#custom-video-play',
      type: 'Videos',
      store: '2.4 GB',
      store_bg: 'bg-warn-500'
    },
    {
      background: ' bg-success-50',
      color: 'text-success-500',
      icon: '#custom-image',
      type: 'Images',
      store: '2.4 GB',
      store_bg: 'bg-success-500'
    }
  ];
  recent_file = [
    {
      title: 'Documents',
      file: '24 files'
    },
    {
      title: 'Root',
      file: '50 files'
    },
    {
      title: 'Images',
      file: '100 files'
    },
    {
      title: 'Music and video',
      file: '100 files'
    }
  ];
  file_card = [
    {
      img: 'assets/images/application/img-file-xls.svg'
    },
    {
      img: 'assets/images/application/img-file-pdf.svg'
    },
    {
      img: 'assets/images/application/img-file-xls.svg'
    },
    {
      img: 'assets/images/application/img-file-rar.svg'
    },
    {
      img: 'assets/images/application/img-file-imgview.jpg'
    },
    {
      img: 'assets/images/application/img-file-ppt.svg'
    },
    {
      img: 'assets/images/application/img-file-ai.svg'
    },
    {
      img: 'assets/images/application/img-file-ppt.svg'
    },
    {
      img: 'assets/images/application/img-file-txt.svg'
    },
    {
      img: 'assets/images/application/img-file-img.svg'
    },
    {
      img: 'assets/images/application/img-file-doc.svg'
    },
    {
      img: 'assets/images/application/img-file-rar.svg'
    },
    {
      img: 'assets/images/application/img-file-doc.svg'
    },
    {
      img: 'assets/images/application/img-file-ppt.svg'
    },
    {
      img: 'assets/images/application/img-file-ai.svg'
    }
  ];
}
