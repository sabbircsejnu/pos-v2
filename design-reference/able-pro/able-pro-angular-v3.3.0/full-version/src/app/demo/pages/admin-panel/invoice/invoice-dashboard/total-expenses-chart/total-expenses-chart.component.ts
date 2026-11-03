// angular import
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// apexChart
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-total-expenses-chart',
  imports: [SharedModule, NgApexchartsModule, CommonModule],
  templateUrl: './total-expenses-chart.component.html',
  styleUrl: './total-expenses-chart.component.scss'
})
export class TotalExpensesChartComponent implements OnInit {
  // public props
  chartOptions!: Partial<ApexOptions>;

  // life cycle hook
  ngOnInit(): void {
    this.chartOptions = {
      chart: {
        height: 280,
        type: 'donut'
      },
      series: [27, 23, 20, 17],
      colors: ['#faad14', '#52c41a', '#ff4d4f', '#1677ff'],
      labels: ['Pending', 'Paid', 'Overdue', 'Draft'],
      fill: {
        opacity: [1, 1, 1, 0.3]
      },
      legend: {
        show: false
      },
      plotOptions: {
        pie: {
          donut: {
            size: '65%',
            labels: {
              show: true,
              name: {
                show: true
              },
              value: {
                show: true
              }
            }
          }
        }
      },
      dataLabels: {
        enabled: false
      },
      responsive: [
        {
          breakpoint: 575,
          options: {
            chart: {
              height: 250
            },
            plotOptions: {
              pie: {
                donut: {
                  size: '65%',
                  labels: {
                    show: false
                  }
                }
              }
            }
          }
        }
      ]
    };
  }

  // public method
  expenses = [
    {
      title: 'Pending',
      value: '$3,202',
      color: 'text-warning-500'
    },
    {
      title: 'Paid',
      value: '$45,050',
      color: 'text-success-500'
    },
    {
      title: 'Overdue',
      value: '$25,000',
      color: 'text-warn-500'
    },
    {
      title: 'Draft',
      value: '$7,694',
      color: ' text-primary-100'
    }
  ];
}
