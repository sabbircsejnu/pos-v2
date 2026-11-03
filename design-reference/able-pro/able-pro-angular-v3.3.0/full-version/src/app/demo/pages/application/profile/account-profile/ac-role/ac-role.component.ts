// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { roleData } from 'src/app/fake-data/ac_role_data';

export interface PeriodicElement {
  name: string;
  img: string;
  text: string;
  role: string;
  role_type: string;
  status_send: string;
  status_type: string;
  status: string;
}

const ELEMENT_DATA: PeriodicElement[] = roleData;

@Component({
  selector: 'app-ac-role',
  imports: [CommonModule, SharedModule],
  templateUrl: './ac-role.component.html',
  styleUrls: ['../account-profile.scss', './ac-role.component.scss']
})
export class AcRoleComponent {
  displayedColumns: string[] = ['name', 'role', 'status', 'more'];
  dataSource = ELEMENT_DATA;
}
