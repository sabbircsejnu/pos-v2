// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { DialogDataExampleDialogComponent } from 'src/app/@theme/components/modal-dialog/modal-dialog.component';
import { MatDialog } from '@angular/material/dialog';

// third party
import { CarouselModule, OwlOptions } from 'ngx-owl-carousel-o';

@Component({
  selector: 'app-social-summary',
  imports: [CommonModule, SharedModule, CarouselModule],
  templateUrl: './social-summary.component.html',
  styleUrls: ['./social-summary.component.scss']
})
export class SocialSummaryComponent {
  dialog = inject(MatDialog);

  // public props
  currentSlide = 'slide-1';

  // public method
  customOptions: OwlOptions = {
    loop: true,
    dots: false,
    mouseDrag: false,
    touchDrag: false,
    pullDrag: false,
    items: 1,
    nav: false,
    rtl: false,
    navText: ['', ''],
    responsive: {
      0: {
        items: 1
      },
      600: {
        items: 1
      },
      900: {
        items: 1
      }
    }
  };

  bio = [
    {
      text: 'https://www.phoenixcoded.net/',
      icon: 'ti ti-world'
    },
    {
      text: 'Hanoi, Vietnam',
      icon: 'ti ti-home-2'
    },
    {
      text: 'August, 21,1996',
      icon: 'ti ti-calendar-event'
    },
    {
      text: 'demo123@mail.com',
      icon: 'ti ti-mail'
    }
  ];

  follow = [
    {
      bg_color: 'bg-primary-50',
      color: 'text-primary-500',
      icon: 'people_alt',
      number: '239K',
      text: 'Followers'
    },
    {
      bg_color: 'bg-warning-50',
      color: 'text-warning-500',
      icon: 'emoji_emotions',
      number: '539K',
      text: 'Following'
    },
    {
      bg_color: 'bg-warn-50',
      color: 'text-warn-500',
      icon: 'favorite',
      number: '539K',
      text: 'Like'
    },
    {
      bg_color: 'bg-success-50',
      color: 'text-success-500',
      icon: 'history_edu',
      number: '400',
      text: 'Post'
    }
  ];

  gallery = [
    {
      img: 'assets/images/application/img-post-1.jpg'
    },
    {
      img: 'assets/images/application/img-post-2.jpg'
    },
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
    },
    {
      img: 'assets/images/application/img-post-1.jpg'
    },
    {
      img: 'assets/images/application/img-post-2.jpg'
    },
    {
      img: 'assets/images/application/img-post-3.jpg'
    }
  ];

  contact = [
    {
      img: 'assets/images/user/avatar-1.png',
      name: 'John Doe'
    },
    {
      img: 'assets/images/user/avatar-2.png',
      name: 'Addie Bass'
    },
    {
      img: 'assets/images/user/avatar-3.png',
      name: 'Alberta Robbins'
    },
    {
      img: 'assets/images/user/avatar-4.jpg',
      name: 'Agnes McGee'
    },
    {
      img: 'assets/images/user/avatar-5.jpg',
      name: 'Deepen Handgun'
    }
  ];

  follow_list = [
    {
      img: 'assets/images/user/avatar-1.png',
      name: 'John Doe',
      email: '@John Doe'
    },
    {
      img: 'assets/images/user/avatar-2.png',
      name: 'Addie Bass',
      email: '@A_Bass'
    },
    {
      img: 'assets/images/user/avatar-3.png',
      name: 'Alberta Robbins',
      email: '@AlbeRob12'
    },
    {
      img: 'assets/images/user/avatar-4.jpg',
      name: 'Agnes McGee',
      email: '@AgnMcGee'
    }
  ];

  openDialog(path: string) {
    this.dialog.open(DialogDataExampleDialogComponent, {
      data: {
        imagePath: path
      },
      panelClass: 'custom-modalBox'
    });
  }
}
