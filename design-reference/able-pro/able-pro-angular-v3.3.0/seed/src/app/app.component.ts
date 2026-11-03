// angular import
import { OnInit, Component, inject, signal } from '@angular/core';
import { Router, NavigationStart, NavigationEnd, NavigationCancel, NavigationError, ActivatedRoute, RouterOutlet } from '@angular/router';

// project import
import { BuyNowLinkService } from './@theme/services/buy-now-link.service';
import { MatProgressBar } from '@angular/material/progress-bar';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
  imports: [RouterOutlet, MatProgressBar]
})
export class AppComponent implements OnInit {
  private router = inject(Router);
  activeRoute = inject(ActivatedRoute);
  private productIdService = inject(BuyNowLinkService);

  // public props
  isSpinnerVisible = signal(true);

  ngOnInit() {
    // Use ngOnInit instead of ngAfterViewInit
    this.router.events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        this.isSpinnerVisible.set(true);
      } else if (event instanceof NavigationEnd || event instanceof NavigationCancel || event instanceof NavigationError) {
        this.isSpinnerVisible.set(false);
      }
    });

    const queryString = window.location.search;
    const params = new URLSearchParams(queryString);
    this.productIdService.setBuyNowLink(params);
  }
}
