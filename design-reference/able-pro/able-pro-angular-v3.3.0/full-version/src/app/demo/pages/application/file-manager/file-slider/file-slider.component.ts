// angular import
import { Component, OnInit, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDrawer } from '@angular/material/sidenav';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { FileManagerLayoutService } from '../file-manager-layout.service';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

@Component({
  selector: 'app-file-slider',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './file-slider.component.html',
  styleUrls: ['./file-slider.component.scss']
})
export class FileSliderComponent implements OnInit {
  // inject service
  private layoutService = inject(FileManagerLayoutService);

  // public props
  readonly file = viewChild<MatDrawer>('file');

  // life cycle hook
  ngOnInit() {
    this.layoutService.fileSlider.subscribe(() => {
      this.file()!.toggle();
    });
  }

  // public method
  fileClose() {
    this.layoutService.toggleFileSide();
  }

  user_file = [
    {
      img: 'assets/images/user/avatar-1.png',
      name: 'John Doe',
      email: 'John_Doe@ablepro.io'
    },
    {
      img: 'assets/images/user/avatar-5.jpg',
      name: 'Addie Bass',
      email: 'Addie_B@ablepro.io'
    },
    {
      img: 'assets/images/user/avatar-3.png',
      name: 'Alberta Robbins',
      email: 'Alberta@ablepro.io'
    },
    {
      img: 'assets/images/user/avatar-2.png',
      name: 'Agnes McGee',
      email: 'Agnes.Gee@ablepro.io'
    }
  ];
}
