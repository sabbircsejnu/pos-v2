// angular import
import { AfterViewInit, Component, viewChild } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { CommonModule } from '@angular/common';
import { MatTableDataSource } from '@angular/material/table';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { revenueData } from 'src/app/fake-data/revenu_data';
import { newCustomerData } from 'src/app/fake-data/new_customer';
import { ticketData } from 'src/app/fake-data/ticket_data';
import { monthlyData } from 'src/app/fake-data/monthly_data';
import { productData } from 'src/app/fake-data/product_data';
import { incomeData } from 'src/app/fake-data/incoming';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

export interface productDataType {
  img: string;
  price: number;
  name: string;
  sales: string;
  status: string;
  status_type: string;
  color: string;
  icon: string;
  rating: string;
  total_reviewer: number;
  text: string;
}
export interface Monthly_Data {
  customer: string;
  plan: string;
  MRR: string;
  color: string;
  icon: string;
}

export interface Ticket_Data {
  subject: string;
  department: string;
  date: string;
  status: string;
}
export interface Revenue_Data {
  icon: string;
  name: string;
  value: string;
  color: string;
}

const ELEMENT_DATA: productDataType[] = productData;
const Monthly_Data = monthlyData;
const Ticket_Data = ticketData;
const Revenue_Data = revenueData;

@Component({
  selector: 'app-widget-data',
  imports: [CommonModule, SharedModule, NgxScrollbar],
  templateUrl: './widget-data.component.html',
  styleUrls: ['./widget-data.component.scss']
})
export class WidgetDataComponent implements AfterViewInit {
  // public props
  readonly paginator = viewChild(MatPaginator);

  // table data come
  displayedColumns: string[] = ['name', 'status', 'price', 'sales', 'rating'];
  dataSource = new MatTableDataSource(ELEMENT_DATA);

  displayedColumns_2: string[] = ['customer', 'plan', 'mrr'];
  dataSource_2 = Monthly_Data;

  recentTicket: string[] = ['subject', 'department', 'date', 'status'];
  ticketSource = Ticket_Data;

  totalRevenue: string[] = ['icon', 'name', 'value'];
  RevenueSource = Revenue_Data;

  // public method
  incoming = incomeData;
  new_customers = newCustomerData;

  my_task = [
    {
      icon: 'ti ti-archive',
      status: 'Sending Report',
      background: 'bg-warn-500',
      color: 'text-success-500'
    },
    {
      icon: 'ti ti-folder',
      status: 'Received Report',
      background: 'bg-success-500',
      color: 'text-muted'
    },
    {
      icon: 'ti ti-archive',
      status: 'Sending Report',
      background: 'bg-warn-500',
      color: 'text-muted'
    }
  ];

  team_member = [
    {
      img: 'assets/images/user/avatar-1.png',
      name: 'David Jones',
      position: 'Project Leader',
      status: '5 min ago'
    },
    {
      img: 'assets/images/user/avatar-2.png',
      name: 'Robert Smith',
      position: 'HR Manager',
      status: 'Yesterday'
    },
    {
      img: 'assets/images/user/avatar-3.png',
      name: 'John larry',
      position: 'Developer',
      status: '1 hour ago'
    },
    {
      img: 'assets/images/user/avatar-4.jpg',
      name: 'David Jones',
      position: 'UI/UX Designer',
      status: '02-05-2022'
    }
  ];

  List_transaction = [
    {
      icon: 'AI',
      name: 'Apple Inc.',
      time: '#ABLE-PRO-T00232',
      amount: '$210,000',
      amount_position: 'ti ti-arrow-down-left',
      percentage: '10.6%',
      amount_type: 'text-warn-500'
    },
    {
      icon: 'SM',
      tooltip: '10,000 Tracks',
      name: 'Spotify Music',
      time: '#ABLE-PRO-T10232',
      amount: '- 10,000',
      amount_position: 'ti ti-arrow-up-right',
      percentage: '30.6%',
      amount_type: 'text-success-500'
    },
    {
      icon: 'MD',
      bg: 'text-primary-500 bg-primary-50',
      tooltip: '143 Posts',
      name: 'Medium',
      time: '06:30 pm',
      amount: '-26',
      amount_position: 'ti ti-arrows-left-right',
      percentage: '5%',
      amount_type: 'text-warning-500'
    },
    {
      icon: 'U',
      tooltip: '143 Posts',
      name: 'Uber',
      time: '08:40 pm',
      amount: '+210,000',
      amount_position: 'ti ti-arrow-up-right',
      percentage: '10.6%',
      amount_type: 'text-success-500'
    },
    {
      icon: 'OC',
      bg: 'text-warning-500 bg-warning-50',
      tooltip: '143 Posts',
      name: 'Ola Cabs',
      time: '07:40 pm',
      amount: '+210,000',
      amount_position: 'ti ti-arrow-up-right',
      percentage: '10.6%',
      amount_type: 'text-success-500'
    }
  ];

  payment = [
    {
      img: 'assets/images/widget/img-paypal.png',
      payment_name: 'Paypal',
      amount: '+210,000',
      color: 'text-success-500',
      percentage: '+ 30.6%'
    },
    {
      img: 'assets/images/widget/img-gpay.png',
      payment_name: 'Gpay',
      amount: '-$2,000',
      color: 'text-warn-500',
      percentage: '- 30.6%'
    },
    {
      img: 'assets/images/widget/img-phonepay.png',
      payment_name: 'Phone Pay',
      amount: '-$2,000',
      color: 'text-warn-500',
      percentage: '- 30.6%'
    }
  ];

  applyFilter(event: Event) {
    const filterValue = (event.target as HTMLInputElement).value;
    this.dataSource.filter = filterValue.trim().toLowerCase();
  }

  // life cycle event
  ngAfterViewInit() {
    this.dataSource.paginator = this.paginator()!;
  }
}
