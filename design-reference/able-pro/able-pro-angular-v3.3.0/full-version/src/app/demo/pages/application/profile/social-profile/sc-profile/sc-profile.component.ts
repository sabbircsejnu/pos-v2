// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { DialogDataExampleDialogComponent } from 'src/app/@theme/components/modal-dialog/modal-dialog.component';

@Component({
  selector: 'app-sc-profile',
  imports: [CommonModule, SharedModule],
  templateUrl: './sc-profile.component.html',
  styleUrls: ['./sc-profile.component.scss']
})
export class ScProfileComponent {
  dialog = inject(MatDialog);

  // public props
  userDetails =
    'Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industrys standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book. It has survived not only five centuries, but also the leap into electronic typesetting, remaining essentially unchanged. It was popularised in the 1960s with the release of Letraset sheets containing Lorem Ipsum passages, and more recently with desktop publishing software like Aldus PageMaker including versions of Lorem Ipsum.';

  userComment =
    "Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry's standard dummy text ever since the 1500s, when an unknown printer took a galley of type and scrambled it to make a type specimen book.";

  // public method
  openDialog(path: string) {
    this.dialog.open(DialogDataExampleDialogComponent, {
      data: {
        imagePath: path
      },
      panelClass: 'custom-modalBox'
    });
  }

  userImages = [
    {
      img: 'assets/images/application/img-story-2.jpg',
      user: 'assets/images/user/avatar-1.png'
    },
    {
      img: 'assets/images/application/img-story-3.jpg',
      user: 'assets/images/user/avatar-2.png'
    },
    {
      img: 'assets/images/application/img-story-4.jpg',
      user: 'assets/images/user/avatar-3.png'
    },
    {
      img: 'assets/images/application/img-story-5.jpg',
      user: 'assets/images/user/avatar-4.jpg'
    },
    {
      img: 'assets/images/application/img-story-6.jpg',
      user: 'assets/images/user/avatar-5.jpg'
    },
    {
      img: 'assets/images/application/img-story-2.jpg',
      user: 'assets/images/user/avatar-1.png'
    },
    {
      img: 'assets/images/application/img-story-3.jpg',
      user: 'assets/images/user/avatar-2.png'
    },
    {
      img: 'assets/images/application/img-story-4.jpg',
      user: 'assets/images/user/avatar-3.png'
    },
    {
      img: 'assets/images/application/img-story-5.jpg',
      user: 'assets/images/user/avatar-4.jpg'
    },
    {
      img: 'assets/images/application/img-story-6.jpg',
      user: 'assets/images/user/avatar-5.jpg'
    }
  ];

  postImages = [
    {
      img: 'assets/images/application/img-post-3.jpg'
    },
    {
      img: 'assets/images/application/img-post-4.jpg'
    },
    {
      img: 'assets/images/application/img-post-5.jpg'
    },
    {
      img: 'assets/images/application/img-post-6.jpg'
    }
  ];
}
