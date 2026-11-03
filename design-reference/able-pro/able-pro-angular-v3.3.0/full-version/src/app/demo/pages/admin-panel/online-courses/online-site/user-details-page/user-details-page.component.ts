// angular import
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { userDetailsData } from 'src/app/fake-data/user-page-data';

export interface page_data {
  title: string;
  url: string;
  state: string;
}

const page_data = userDetailsData;

@Component({
  selector: 'app-user-details-page',
  imports: [SharedModule, CommonModule],
  templateUrl: './user-details-page.component.html',
  styleUrl: './user-details-page.component.scss'
})
export class UserDetailsPageComponent {
  pageData: string[] = ['title', 'url', 'state', 'Action'];
  userDetailsData = page_data;
}
