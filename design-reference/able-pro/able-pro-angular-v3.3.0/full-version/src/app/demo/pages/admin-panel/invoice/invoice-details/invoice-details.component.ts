// angular import
import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { NgxPrintModule } from 'ngx-print';

export interface PeriodicElement {
  id: number;
  name: string;
  description: string;
  qty: number;
  price: number;
}

const ELEMENT_DATA: PeriodicElement[] = [
  {
    id: 1,
    name: 'Mauris',
    description: 'Malesuada adipiscing',
    qty: 2,
    price: 80.0
  },
  {
    id: 2,
    name: 'Vitae',
    description: 'Hac egestas',
    qty: 3,
    price: 40.0
  },
  {
    id: 3,
    name: 'Mauris',
    description: 'Malesuada adipiscing',
    qty: 4,
    price: 80.0
  }
];

@Component({
  selector: 'app-invoice-details',
  imports: [SharedModule, RouterModule, NgxPrintModule],
  templateUrl: './invoice-details.component.html',
  styleUrl: './invoice-details.component.scss'
})
export class InvoiceDetailsComponent {
  // public props
  displayedColumns: string[] = ['id', 'name', 'description', 'qty', 'price', 'amount'];
  dataSource = ELEMENT_DATA;

  // public method
  shareOptions = [
    {
      icon: '#custom-edit2',
      link: '/invoice/edit'
    },
    {
      icon: '#custom-eye'
    },
    {
      icon: '#custom-document-download'
    },
    {
      icon: '#custom-printer'
    },
    {
      icon: '#custom-share-bold'
    }
  ];

  address = [
    {
      type: 'Form',
      name: 'Garcia-Cameron and Sons',
      street: '8534 Saunders Hill Apt. 583',
      phone: '(970) 982-3353',
      email: 'brandon07@pierce.com'
    },
    {
      type: 'To',
      name: 'Dickinson-Cummerata',
      street: '55D Leatha Way Ernaburgh, NT 2146',
      phone: '75-9079921',
      email: 'kasandra.conn@borer.com'
    }
  ];
}
