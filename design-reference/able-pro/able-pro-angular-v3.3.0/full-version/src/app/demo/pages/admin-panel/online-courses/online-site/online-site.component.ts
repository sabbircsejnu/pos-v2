// Angular import
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CdkStepperModule } from '@angular/cdk/stepper';
import { CommonModule } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { SiteDetailsComponent } from './site-details/site-details.component';
import { bioData } from 'src/app/fake-data/bio_data';
import { UserDetailsPageComponent } from './user-details-page/user-details-page.component';
import { BioDetailsComponent } from './bio-details/bio-details.component';

// third party
import { FileUploadControl, FileUploadModule, FileUploadValidators } from '@iplab/ngx-file-upload';

// rxjs
import { BehaviorSubject, Subscription } from 'rxjs';

export interface bio_data {
  name: string;
  src: string;
  date: string;
  time: number;
}

const bio_data = bioData;

@Component({
  selector: 'app-online-site',
  imports: [SharedModule, SiteDetailsComponent, CdkStepperModule, FileUploadModule, CommonModule, UserDetailsPageComponent],
  templateUrl: './online-site.component.html',
  styleUrl: './online-site.component.scss'
})
export class OnlineSiteComponent implements OnInit, OnDestroy {
  dialog = inject(MatDialog);

  // public props
  fileSub = new Subscription();
  uploadedFiles: Array<File> = [];
  newUploadFile: Array<File> = [];

  // private props
  // eslint-disable-next-line
  readonly uploadedFile: BehaviorSubject<any> = new BehaviorSubject(null);

  readonly control = new FileUploadControl({ listVisible: true, accept: ['image/*'], discardInvalid: true, multiple: false }, [
    FileUploadValidators.accept(['image/*']),
    FileUploadValidators.filesLimit(1)
  ]);

  // life cycle
  ngOnInit(): void {
    this.fileSub = this.control.valueChanges.subscribe((values: Array<File>) => this.getImage(values[0]));
  }

  ngOnDestroy(): void {
    this.fileSub.unsubscribe();
  }

  // private method
  private getImage(file: File): void {
    if (FileReader && file) {
      const fr = new FileReader();
      fr.onload = (e) => this.uploadedFile.next(e.target!.result);
      fr.readAsDataURL(file);
    } else {
      this.uploadedFile.next(null);
    }
  }

  bioDetails() {
    this.dialog.open(BioDetailsComponent, {
      width: '500px'
    });
  }

  presetColors = [
    {
      name: 'Preset 1',
      color_type: [
        {
          color: 'text-success-600'
        },
        {
          color: 'text-success-400'
        },
        {
          color: 'text-success-200'
        }
      ]
    },
    {
      name: 'Preset 2',
      color_type: [
        {
          color: 'text-warning-600'
        },
        {
          color: 'text-warning-400'
        },
        {
          color: 'text-warning-200'
        }
      ]
    },
    {
      name: 'Preset 3',
      color_type: [
        {
          color: 'text-primary-600'
        },
        {
          color: 'text-primary-400'
        },
        {
          color: 'text-primary-200'
        }
      ]
    },
    {
      name: 'Preset 4',
      color_type: [
        {
          color: 'text-accent-600'
        },
        {
          color: 'text-accent-400'
        },
        {
          color: 'text-accent-200'
        }
      ]
    }
  ];

  layoutsColor = [
    {
      name: 'Nav Bar & Footer Background',
      subTitle: 'Fixed, scrolling & email',
      color: 'text-success-400',
      code: '#4cb592'
    },
    {
      name: 'Navigation Bar Link',
      subTitle: 'Links when nav bar is fixed',
      color: 'text-warn-300',
      code: '#e76767'
    },
    {
      name: 'Navigation Bar',
      subTitle: 'Links when nav bar is scrolling',
      color: 'text-success-200',
      code: '#96d4bf'
    },
    {
      name: 'Homepage Headings & Subtitle',
      subTitle: 'When a background is set',
      color: 'text-accent-400',
      code: '#bec8d0'
    },
    {
      name: 'Course Page Heading & Subtitle',
      subTitle: 'When a background is set',
      color: 'text-accent-200',
      code: '#f3f5f7'
    },
    {
      name: 'Headings',
      subTitle: '<h1> to <h5>',
      color: 'text-accent-600',
      code: '#5b6b79'
    },
    {
      name: 'Body text',
      subTitle: '<body>, <p>',
      color: 'text-accent-800',
      code: '#1d2630'
    },
    {
      name: 'Buttons & Links',
      subTitle: '<a>, <button>',
      color: 'text-success-500',
      code: '#2ca87f'
    }
  ];

  BioData: string[] = ['Name', 'Date', 'Action'];
  bioData = bio_data;

  schoolPage = [
    {
      title: 'Main Page',
      url: '/main.page',
      status: 'Published',
      background: 'text-bg-success'
    },
    {
      title: 'Login Page',
      url: '/login-page.design',
      status: 'Published',
      background: 'text-bg-success'
    },
    {
      title: 'Privacy Policy',
      url: '/privacy-policy',
      status: 'Unpublished',
      background: 'text-bg-danger'
    },
    {
      title: 'Main Page',
      url: '/main.page',
      status: 'Published',
      background: 'text-bg-success'
    },
    {
      title: 'Login Page',
      url: '/login-page.design',
      status: 'Published',
      background: 'text-bg-success'
    },
    {
      title: 'Privacy Policy',
      url: '/privacy-policy',
      status: 'Unpublished',
      background: 'text-bg-danger'
    },
    {
      title: 'Main Page',
      url: '/main.page',
      status: 'Published',
      background: 'text-bg-success'
    },
    {
      title: 'Login Page',
      url: '/login-page.design',
      status: 'Published',
      background: 'text-bg-success'
    },
    {
      title: 'Privacy Policy',
      url: '/privacy-policy',
      status: 'Unpublished',
      background: 'text-bg-danger'
    }
  ];
}
