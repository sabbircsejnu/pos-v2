// Angular import
import { Component, NgZone, OnInit, inject, input } from '@angular/core';
import { Location } from '@angular/common';

// project import
import { NavigationItem } from 'src/app/@theme/types/navigation';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { MenuCollapseCompactComponent } from '../menu-collapse/menu-collapse.component';
import { MenuItemCompactComponent } from '../menu-item/menu-item.component';

@Component({
  selector: 'app-menu-group-compact',
  imports: [SharedModule, MenuCollapseCompactComponent, MenuItemCompactComponent],
  templateUrl: './menu-group.component.html',
  styleUrls: ['./menu-group.component.scss']
})
export class MenuGroupCompactComponent implements OnInit {
  private zone = inject(NgZone);
  private location = inject(Location);

  // public props

  // All Version in Group Name
  readonly item = input.required<NavigationItem>();

  current_url!: string;

  // Life cycle events
  ngOnInit() {
    this.current_url = this.location.path();
    //eslint-disable-next-line
    //@ts-ignore
    const baseHref = this.location['_baseHref'] || '';
    this.current_url = baseHref + this.current_url;

    // Use a more reliable way to find and update the active group
    setTimeout(() => {
      const links = document.querySelectorAll('a.nav-link') as NodeListOf<HTMLAnchorElement>;
      links.forEach((link: HTMLAnchorElement) => {
        if (link.getAttribute('href') === this.current_url) {
          let parent = link.parentElement;
          while (parent && parent.classList) {
            if (parent.classList.contains('coded-hasmenu')) {
              parent.classList.add('coded-trigger');
              parent.classList.add('active');
            }
            parent = parent.parentElement;
          }
        }
      });
    }, 0);
  }
}
