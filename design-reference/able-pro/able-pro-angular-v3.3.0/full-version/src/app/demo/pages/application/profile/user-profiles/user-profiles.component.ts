// angular import
import { Component, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

// third party
import { ApexOptions, NgApexchartsModule } from 'ng-apexcharts';

// project import
import { TopSvgComponent } from './top-svg/top-svg.component';
import { BackSvgComponent } from './back-svg/back-svg.component';
import { CardComponent } from 'src/app/@theme/components/card/card.component';

// Angular Material
import { MatButton } from '@angular/material/button';
import { MatMenuTrigger, MatMenu, MatMenuItem } from '@angular/material/menu';
import { MatDivider } from '@angular/material/divider';

@Component({
  selector: 'app-user-profiles',
  templateUrl: './user-profiles.component.html',
  styleUrls: ['./user-profiles.component.scss'],
  imports: [
    TopSvgComponent,
    BackSvgComponent,
    NgApexchartsModule,
    MatButton,
    CardComponent,
    MatMenuTrigger,
    MatMenu,
    MatMenuItem,
    MatDivider,
    RouterLink,
    RouterLinkActive,
    RouterOutlet
  ]
})
export class UserProfilesComponent implements OnInit {
  // public props
  totalEarningChart: Partial<ApexOptions>;
  currentTabs = 'personal';

  // life cycle event
  ngOnInit(): void {
    this.totalEarningChart = {
      series: [30],
      chart: {
        height: 150,
        type: 'radialBar'
      },
      plotOptions: {
        radialBar: {
          hollow: {
            margin: 0,
            size: '60%',
            background: 'transparent',
            imageOffsetX: 0,
            imageOffsetY: 0,
            position: 'front'
          },
          track: {
            background: 'var(--primary-200)',
            strokeWidth: '50%'
          },

          dataLabels: {
            show: true,
            name: {
              show: false
            },
            value: {
              offsetY: 7,
              color: 'var(--primary-500)',
              fontSize: '20px',
              fontWeight: '700',
              show: true
            }
          }
        }
      },
      colors: ['var(--primary-500)'],
      fill: {
        type: 'solid'
      },
      stroke: {
        lineCap: 'round'
      }
    };
  }
}
