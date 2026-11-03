// angular import
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { InvoiceListChartComponent } from './invoice-list-chart/invoice-list-chart.component';
import { InvoiceListTableComponent } from './invoice-list-table/invoice-list-table.component';

@Component({
  selector: 'app-invoice-list',
  imports: [SharedModule, CommonModule, InvoiceListChartComponent, InvoiceListTableComponent],
  templateUrl: './invoice-list.component.html',
  styleUrl: './invoice-list.component.scss'
})
export class InvoiceListComponent {
  // public method
  widgetCards = [
    {
      title: 'Paid',
      isLoss: false,
      value: '$7,825',
      percentage: 70.5,
      color: 'text-success-500',
      invoice: '9',
      data: [0, 20, 10, 45, 30, 55, 20, 30],
      colors: ['#2ca87f']
    },
    {
      title: 'Unpaid',
      isLoss: true,
      value: '$1,880',
      percentage: 27.4,
      color: 'text-warning-500',
      invoice: '6',
      data: [30, 20, 55, 30, 45, 10, 20, 0],
      colors: ['#e58a00']
    },
    {
      title: 'Overdue',
      isLoss: true,
      value: '$3,507',
      percentage: 27.4,
      color: 'text-warn-500',
      invoice: '4',
      data: [0, 20, 10, 45, 30, 55, 20, 30],
      colors: ['#dc2626']
    }
  ];
}
