// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-ac-profile',
  imports: [CommonModule, SharedModule],
  templateUrl: './ac-profile.component.html',
  styleUrls: ['../account-profile.scss', './ac-profile.component.scss']
})
export class AcProfileComponent {
  // public method
  userSkills = [
    {
      name: 'Junior',
      progress: '30'
    },
    {
      name: 'UX Researcher',
      progress: '80'
    },
    {
      name: 'Wordpress',
      progress: '90'
    },
    {
      name: 'HTML',
      progress: '30'
    },
    {
      name: 'Graphic Design',
      progress: '95'
    },
    {
      name: 'Code Style',
      progress: '75'
    }
  ];

  person = [
    {
      icon: 'ti ti-mail',
      text: 'anshan@gmail.com'
    },
    {
      icon: 'ti ti-phone',
      text: '(+1-876) 8654 239 581'
    },
    {
      icon: 'ti ti-map',
      text: 'New York'
    }
  ];

  personal_details = [
    {
      space: 'p-t-0',
      group: 'Full Name',
      text: 'Anshan Handgun',
      group_2: 'Father Name',
      text_2: 'Mr. Deepen Handgun'
    },
    {
      group: 'Phone',
      text: '(+1-876) 8654 239 581',
      group_2: 'Country',
      text_2: 'New York'
    },
    {
      group: 'Email',
      text: 'anshan.dh81@gmail.com',
      group_2: 'Zip Code',
      text_2: '956 754'
    }
  ];

  employment = [
    {
      space: 'p-t-0',
      group: 'Senior',
      text: 'Senior UI/UX designer (Year)',
      group_2: 'Job Responsibility',
      text_2:
        'Perform task related to project manager with the 100+ team under my observation. Team management is key role in this company.'
    },
    {
      group: 'Trainee cum Project Manager (Year)',
      text: '2017-2019',
      group_2: 'Job Responsibility',
      text_2: 'Team management is key role in this company.'
    },
    {
      space: 'p-b-0',
      group: 'School (Year)',
      text: '2009-2011',
      group_2: 'Institute',
      text_2: 'School of London, England'
    }
  ];

  education = [
    {
      space: 'p-t-0',
      group: 'Master Degree (Year)',
      text: '2014-2017',
      group_2: 'Institute',
      text_2: '-'
    },
    {
      group: 'Bachelor (Year)',
      text: '2011-2013',
      group_2: 'Institute',
      text_2: 'Imperial College London'
    },
    {
      space: 'p-b-0',
      group: 'School (Year)',
      text: '2009-2011',
      group_2: 'Institute',
      text_2: 'School of London, England'
    }
  ];
}
