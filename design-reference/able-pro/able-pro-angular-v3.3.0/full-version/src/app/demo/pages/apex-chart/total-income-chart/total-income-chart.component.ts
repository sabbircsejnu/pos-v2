// angular import
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { NgApexchartsModule, ApexOptions } from 'ng-apexcharts';

@Component({
  selector: 'app-total-income-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './total-income-chart.component.html',
  styleUrl: './total-income-chart.component.scss'
})
export class TotalIncomeChartComponent {
  // public props
  chartOptions: Partial<ApexOptions>;
  incomeColors = ['var(--primary-500)', '#E58A00', '#2CA87F', 'var(--primary-200)'];

  // constructor
  constructor() {
    this.chartOptions = {
      chart: {
        height: 320,
        type: 'donut'
      },
      series: [27, 23, 20, 17],
      labels: ['Total income', 'Total rent', 'Download', 'Views'],
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

  // public methods
  income_card = [
    {
      background: 'bg-primary-500',
      item: 'Income',
      value: '$23,876',
      number: '+$763,43'
    },
    {
      background: 'bg-warning-500',
      item: 'Rent',
      value: '$23,876',
      number: '+$763,43'
    },
    {
      background: 'bg-success-500',
      item: 'Download',
      value: '$23,876',
      number: '+$763,43'
    },
    {
      background: 'bg-primary-200',
      item: 'Views',
      value: '$23,876',
      number: '+$763,43'
    }
  ];
}
