// angular import
import { Component, OnInit } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// apexChart
import { NgApexchartsModule, ApexStroke, ApexGrid, ApexNonAxisChartSeries, ApexChart, ApexPlotOptions } from 'ng-apexcharts';

export type ChartOptions = {
  series: ApexNonAxisChartSeries;
  chart: ApexChart;
  colors: string[];
  stroke: ApexStroke;
  grid: ApexGrid;
  plotOptions: ApexPlotOptions;
};

@Component({
  selector: 'app-invites-goal-chart',
  imports: [SharedModule, NgApexchartsModule],
  templateUrl: './invites-goal-chart.component.html',
  styleUrl: './invites-goal-chart.component.scss'
})
export class InvitesGoalChartComponent implements OnInit {
  // public props

  chartOptions!: Partial<ChartOptions>;

  // life cycle
  ngOnInit(): void {
    this.chartOptions = {
      series: [76],
      chart: {
        type: 'radialBar',
        height: '345px',
        offsetY: -20,
        sparkline: {
          enabled: true
        }
      },
      colors: ['var(--primary-500)'],
      plotOptions: {
        radialBar: {
          startAngle: -95,
          endAngle: 95,
          hollow: {
            margin: 15,
            size: '50%'
          },
          track: {
            background: '#eaeaea',
            strokeWidth: '97%',
            margin: 20
          },
          dataLabels: {
            name: {
              show: false
            },
            value: {
              offsetY: 0,
              fontSize: '20px'
            }
          }
        }
      },
      grid: {
        padding: {
          top: 10
        }
      },
      stroke: {
        lineCap: 'round'
      }
    };
  }
}
