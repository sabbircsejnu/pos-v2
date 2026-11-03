// angular import
import { Component, OnInit, OnDestroy, Renderer2, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// third party
import { CarouselModule, OwlOptions } from 'ngx-owl-carousel-o';

// project import
import { SharedModule } from '../../shared/shared.module';
import { BuyNowLinkService } from '../../../@theme/services/buy-now-link.service';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

// data
import { techData } from '../../data/tech-data';

// type
import { TechSection } from 'src/app/@theme/types/tech-data-type';

// const
import { RTL } from 'src/app/@theme/const';

@Component({
  selector: 'app-landing',
  imports: [CommonModule, SharedModule, RouterModule, CarouselModule],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss']
})
export class LandingComponent implements OnInit, OnDestroy {
  private renderer = inject(Renderer2);
  buyNowLinkService = inject(BuyNowLinkService);
  private themeService = inject(ThemeLayoutService);

  // public props
  currentSlide = 'slide-1';
  scrolledPastPoint = false;
  single: boolean;
  dropDownIcon: string = 'custom-arrowDown2';
  techDetails: TechSection[] = techData;
  isDark: boolean = false;

  // constructor
  constructor() {
    effect(() => {
      this.themeDirection(this.themeService.directionChange());
      this.DarkTheme(this.themeService.isDarkMode());
    });
  }

  // life cycle event
  ngOnInit() {
    this.renderer.addClass(document.body, 'landing-page');
    // landing page menu sticky
    let ost = 0;
    window.addEventListener('scroll', () => {
      const cOst = document.documentElement.scrollTop;
      const header = document.querySelector('.component-header') as HTMLElement;

      if (cOst === 0) {
        header.classList.add('top-header');
      } else if (cOst > ost) {
        header.classList.add('top-header');
        header.classList.remove('default');
      } else {
        header.classList.add('default');
        header.classList.remove('top-header');
      }
      ost = cOst;
    });
  }

  ngOnDestroy() {
    this.renderer.removeClass(document.body, 'landing-page');
  }

  // public method

  private themeDirection(direction: string) {
    this.applicationPageSlider = { ...this.applicationPageSlider, rtl: direction === RTL ? true : false };
  }

  private DarkTheme(isDark: string) {
    this.isDark = isDark === 'dark' ? true : false;
  }

  // application page slider
  applicationPageSlider: OwlOptions = {
    loop: true,
    dots: false,
    mouseDrag: false,
    touchDrag: true,
    pullDrag: false,
    items: 1,
    nav: false,
    rtl: true,
    navText: ['', ''],
    responsive: {
      0: {
        items: 1
      },
      600: {
        items: 1
      },
      900: {
        items: 1
      }
    }
  };

  links = [
    { id: 'slide-1', title: 'Chat', description: 'Power your web apps with the conceptual chat app of Able Pro dashboard template.' },
    {
      id: 'slide-2',
      title: 'E-commerce',
      description: 'Collection, filter, product detail, add new product, and checkout pages makes your e-commerce app complete.'
    },
    {
      id: 'slide-3',
      title: 'Inbox',
      description: 'Compose message, list message (email), detailed inbox pages well suited for any conversation based web apps.'
    },
    {
      id: 'slide-4',
      title: 'User Management',
      description: 'Detailed pages for User management like profile settings, role, account settings, social profile and more to explore.'
    }
  ];

  carouselData = [
    { id: 'slide-1', img: 'assets/images/landing/Chat.png', imgDark: 'assets/images/landing/chat-dark.png' },
    { id: 'slide-2', img: 'assets/images/landing/e-commerce.png', imgDark: 'assets/images/landing/e-commerce-dark.png' },
    { id: 'slide-3', img: 'assets/images/landing/mail.png', imgDark: 'assets/images/landing/mail-dark.png' },
    { id: 'slide-4', img: 'assets/images/landing/social.png', imgDark: 'assets/images/landing/social-dark.png' }
  ];

  support = [
    {
      img: 'assets/images/user/avatar-1.png',
      text: '“I get all what I need for my project from this template so I can focus to back end side. The template looks fantastic and the support is fast. Thank you.“',
      author: 'menhook',
      describe: 'Feature Availability'
    },
    {
      img: 'assets/images/user/avatar-2.png',
      text: '“Code quality is amazing. Design is astonishing. very easy to customize .. and any developer can use it with ease.“',
      author: 'shahabblouch',
      describe: 'Code Quality'
    },
    {
      img: 'assets/images/user/avatar-3.png',
      text: '“Design is very good.“',
      author: 'dimas_ferian',
      describe: 'Design Quality'
    },
    {
      img: 'assets/images/user/avatar-4.jpg',
      text: '“Amazing template for fast develop“',
      author: 'devbardbudist',
      describe: 'Customizability'
    },
    {
      img: 'assets/images/user/avatar-5.jpg',
      text: '“The author is very nice and solved my problem inmediately“',
      author: 'richitela',
      describe: 'Customer Support'
    },
    {
      img: 'assets/images/user/avatar-1.png',
      text: '“An amazing template, Very good design, good quality code and also very good customer support.“',
      author: 'macugi',
      describe: 'Code Quality'
    },
    {
      img: 'assets/images/user/avatar-2.png',
      text: '“The author is very nice and solved my problem inmediately 😍“',
      author: 'richitela',
      describe: 'Customer Support'
    },
    {
      img: 'assets/images/user/avatar-3.png',
      text: '“Very universal admin template“',
      author: 'htmhell',
      describe: 'Design Quality'
    },
    {
      img: 'assets/images/user/avatar-4.jpg',
      text: '“An amazing template. Very good design, good quality code and also very good customer support. 💎“',
      author: 'macugi',
      describe: 'Code Quality'
    },
    {
      img: 'assets/images/user/avatar-5.jpg',
      text: '“I have it running on a medium size site that is geared towards displaying stats tables and custom forms, a blog and a forum. My customers love the design and the speed in which the pages load. 😍 “',
      author: 'RizzoFrank',
      describe: 'Design Quality'
    }
  ];

  trustedBy = [
    {
      images: 'assets/images/landing/client-eagames.svg'
    },
    {
      images: 'assets/images/landing/client-vodafone.svg'
    },
    {
      images: 'assets/images/landing/client-crystal-1.svg'
    },
    {
      images: 'assets/images/landing/client-haswent-2.svg'
    },
    {
      images: 'assets/images/landing/client-haxter-3.svg'
    },
    {
      images: 'assets/images/landing/client-totalstudio-5.svg'
    },
    {
      images: 'assets/images/landing/client-montecito-4.svg'
    },
    {
      images: 'assets/images/landing/client-slingshot.svg'
    }
  ];

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

  socialLinks = [
    { url: 'https://github.com/phoenixcoded', icon: 'ti ti-brand-github' },
    { url: 'https://dribbble.com/Phoenixcoded', icon: 'ti ti-brand-dribbble' },
    { url: 'https://www.youtube.com/@phoenixcoded', icon: 'ti ti-brand-youtube' }
  ];

  toggleIcon(): void {
    this.dropDownIcon = 'custom-arrowUp2';
  }

  resetIcon(): void {
    this.dropDownIcon = 'custom-arrowDown2';
  }
}
