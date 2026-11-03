// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ScProfileComponent } from './sc-profile/sc-profile.component';
import { SocialSummaryComponent } from './social-summary/social-summary.component';
import { ScGalleryComponent } from './sc-gallery/sc-gallery.component';
import { friendList } from 'src/app/fake-data/sc_friends';

@Component({
  selector: 'app-social-profile',
  imports: [CommonModule, SharedModule, ScProfileComponent, SocialSummaryComponent, ScGalleryComponent],
  templateUrl: './social-profile.component.html',
  styleUrls: ['./social-profile.component.scss']
})
export class SocialProfileComponent {
  // public props
  friends_list = friendList;
}
