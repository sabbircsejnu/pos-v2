// angular import
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, Validators } from '@angular/forms';
import { BreakpointObserver } from '@angular/cdk/layout';

// project import
import { SharedModule } from '../../shared/shared.module';
import { BuyNowLinkService } from 'src/app/@theme/services/buy-now-link.service';

@Component({
  selector: 'app-contact-us',
  imports: [CommonModule, SharedModule],
  templateUrl: './contact-us.component.html',
  styleUrls: ['./contact-us.component.scss']
})
export class ContactUsComponent {
  buyNowLinkService = inject(BuyNowLinkService);
  // public props
  breakpoint = 12;
  isMobile = false;
  selectedOption = '1';
  email = new FormControl('', [Validators.required, Validators.email]);
  mobileNumberFormControl = new FormControl('', [Validators.minLength(10)]);

  // constructor
  constructor() {
    const breakpointObserver = inject(BreakpointObserver);

    breakpointObserver.observe(['(max-width: 767px)']).subscribe((result) => {
      this.isMobile = result.matches;
    });
  }

  // public method
  getErrorMessage() {
    if (this.email.hasError('required')) {
      return 'You must enter a value';
    }

    return this.email.hasError('email') ? 'Not a valid email' : '';
  }

  footerSection = [
    {
      title: this.buyNowLinkService.isPhoenix ? 'Company' : 'Able Pro Eco-System',
      footerLink: this.buyNowLinkService.isPhoenix
        ? [
            {
              title: 'Profile',
              link: 'https://1.envato.market/xk3bQd'
            },
            {
              title: 'Portfolio',
              link: 'https://1.envato.market/Qyre4x'
            },
            {
              title: 'Follow Us',
              link: 'https://1.envato.market/Py9k4X'
            },
            {
              title: 'Website',
              link: 'https://www.phoenixcoded.net/'
            }
          ]
        : [
            {
              title: 'Bundle',
              link: 'https://codedthemes.com/item/able-pro-dashboard-templates'
            },
            {
              title: 'Bootstrap',
              link: 'https://codedthemes.com/item/able-pro-bootstrap-admin-template/'
            },
            {
              title: 'Angular',
              link: 'https://codedthemes.com/item/able-pro-angular-admin-template/'
            },
            {
              title: 'React',
              link: 'https://codedthemes.com/item/able-pro-mui-react-admin-template/'
            },
            {
              title: 'Next',
              link: 'https://codedthemes.com/item/able-pro-nextjs-mui-react-admin-template/'
            },
            {
              title: 'Tailwind',
              link: 'https://codedthemes.com/item/able-pro-tailwind-css-admin-dashboard/'
            },
            {
              title: 'Vue',
              link: 'https://codedthemes.com/item/able-pro-vuejs-admin-dashboard/ ‎'
            },
            {
              title: 'Vue + Laravel',
              link: 'https://codedthemes.com/item/able-pro-vue-laravel-admin-dashboard'
            }
          ]
    },
    {
      title: 'Help & Support',
      footerLink: [
        {
          title: 'Documentation',
          link: 'https://phoenixcoded.gitbook.io/able-pro'
        },
        {
          title: 'Feature Request',
          link: this.buyNowLinkService.isPhoenix ? 'https://phoenixcoded.authordesk.app/' : 'https://codedthemes.support-hub.io/'
        },
        {
          title: 'RoadMap',
          link: 'https://phoenixcoded.gitbook.io/able-pro/roadmap'
        },
        {
          title: 'Support',
          link: this.buyNowLinkService.isPhoenix ? 'https://phoenixcoded.authordesk.app/' : 'https://codedthemes.support-hub.io/'
        },
        {
          title: 'Discord',
          link: 'https://discord.com/invite/gmqz4BWYA8'
        }
      ]
    },
    {
      title: 'Useful Resources',
      footerLink: [
        {
          title: 'Price Policy',
          link: this.buyNowLinkService.isPhoenix
            ? 'https://themeforest.net/page/item_support_policy'
            : 'https://codedthemes.com/privacy-policy/'
        },
        {
          title: 'Licenses Term',
          link: this.buyNowLinkService.isPhoenix ? 'https://themeforest.net/licenses/standard' : 'https://codedthemes.com/license/'
        }
      ]
    }
  ];
}
