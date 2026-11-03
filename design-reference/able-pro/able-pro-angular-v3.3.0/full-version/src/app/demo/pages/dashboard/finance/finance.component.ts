// angular import
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { TransactionsChartComponent } from './transactions-chart/transactions-chart.component';
import { CashFlowChartComponent } from './cash-flow-chart/cash-flow-chart.component';
import { CategoryChartComponent } from './category-chart/category-chart.component';
import { transactionsHistoryList } from 'src/app/fake-data/transactions_history_list';

export interface history_data {
  image: string;
  name: string;
  category: string;
  date: string;
  time: string;
  amount: string;
  status: string;
}

const history_data = transactionsHistoryList;

@Component({
  selector: 'app-finance',
  imports: [SharedModule, CommonModule, TransactionsChartComponent, CashFlowChartComponent, CategoryChartComponent],
  templateUrl: './finance.component.html',
  styleUrl: './finance.component.scss'
})
export class FinanceComponent {
  history: string[] = ['name', 'category', 'date', 'amount', 'status', 'action'];
  historySource = history_data;

  // public method
  transactionsList = [
    {
      icon: 'AI',
      title: 'Apple Inc.',
      description: '#ABLE-PRO-T00232',
      amount: '$210,000',
      progress: 'ti ti-arrow-down-left',
      status_color: 'text-warn-500',
      percentage: 10.6
    },
    {
      icon: 'SM',
      title: 'Spotify Music',
      description: '#ABLE-PRO-T10232',
      amount: '- 10,000',
      progress: 'ti ti-arrow-up-right',
      status_color: 'text-success-500',
      percentage: 30.6
    },
    {
      icon: 'MD',
      background: 'bg-primary-50 text-primary-500',
      title: 'Medium',
      description: '06:30 pm',
      amount: '- 26',
      progress: 'ti ti-arrows-left-right',
      status_color: 'text-warning-500',
      percentage: 5
    },
    {
      icon: 'U',
      title: 'Uber',
      description: '08:40 pm',
      amount: '+210,000',
      progress: 'ti ti-arrow-up-right',
      status_color: 'text-success-500',
      percentage: 10.6
    },
    {
      icon: 'OC',
      background: 'bg-warning-50 text-warning-500',
      title: 'Ola Cabs',
      description: '07:40 pm',
      amount: '+210,000',
      progress: 'ti ti-arrow-up-right',
      status_color: 'text-success-500',
      percentage: 10.6
    }
  ];

  moneyGoList = [
    {
      image: 'assets/images/widget/img-food.png',
      title: 'Food & Drink',
      progress: 65,
      total: '$1000'
    },
    {
      image: 'assets/images/widget/img-travel.png',
      title: 'Travel',
      progress: 30,
      total: '$400'
    },
    {
      image: 'assets/images/widget/img-shopping.png',
      title: 'Shopping',
      progress: 52,
      total: '$900'
    },
    {
      image: 'assets/images/widget/img-health.png',
      title: 'Healthcare',
      progress: 26,
      total: '$250'
    }
  ];

  accountCardList = [
    {
      image: 'assets/images/widget/img-card-1.png',
      addNewCard: false
    },
    {
      image: 'assets/images/widget/img-card-2.png',
      addNewCard: false
    },
    {
      addNewCard: true
    }
  ];

  recentTransfer = [
    {
      amount: '-$750.00',
      color: 'text-warn-500',
      name: 'Starbucks Coffee',
      date: 'yesterday',
      image: 'assets/images/widget/img-acitivity-3.svg'
    },
    {
      amount: '-$26.00',
      color: 'text-success-500',
      name: 'Liberty Trading',
      date: '10:40 AM',
      image: 'assets/images/widget/liberty.svg'
    },
    {
      amount: '-29.00',
      color: 'text-warn-500',
      name: 'Croma',
      date: '05:41 PM',
      image: 'assets/images/widget/croma.svg'
    }
  ];

  userList = [
    {
      newUserAdd: true
    },
    {
      newUserAdd: false,
      image: 'assets/images/user/avatar-1.png'
    },
    {
      newUserAdd: false,
      image: 'assets/images/user/avatar-2.png'
    },
    {
      newUserAdd: false,
      image: 'assets/images/user/avatar-3.png'
    },
    {
      newUserAdd: false,
      image: 'assets/images/user/avatar-4.jpg'
    }
  ];
}
