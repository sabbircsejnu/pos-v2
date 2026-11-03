// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { BuyNowLinkService } from '../../services/buy-now-link.service';

@Component({
  selector: 'app-footer',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './footer.component.html',
  styleUrls: ['./footer.component.scss']
})
export class FooterComponent {
  buyNowLinkService = inject(BuyNowLinkService);
}
