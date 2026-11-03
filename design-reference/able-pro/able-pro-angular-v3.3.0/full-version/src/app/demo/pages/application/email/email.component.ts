// angular import
import { Component, OnInit, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatDrawer, MatDrawerMode } from '@angular/material/sidenav';
import { MatDialog } from '@angular/material/dialog';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';
import { MailContentComponent } from './mail-content/mail-content.component';
import { ComposeMailComponent } from './compose-mail/compose-mail.component';
import { MIN_WIDTH_1025PX, MAX_WIDTH_1024PX, MAX_WIDTH_1399PX, MIN_WIDTH_1400PX } from 'src/app/@theme/const';

@Component({
  selector: 'app-email',
  imports: [CommonModule, SharedModule, MailContentComponent, NgxScrollbar],
  templateUrl: './email.component.html',
  styleUrls: ['./email.component.scss']
})
export class EmailComponent implements OnInit {
  private breakpointObserver = inject(BreakpointObserver);
  dialog = inject(MatDialog);

  // public props
  readonly email = viewChild<MatDrawer>('email');
  modeValue: MatDrawerMode = 'side';
  status = 'true';
  selectedTabIndex = 0;
  mailListHight = false;

  // life cycle event
  ngOnInit() {
    this.breakpointObserver.observe([MIN_WIDTH_1025PX, MAX_WIDTH_1024PX]).subscribe((result) => {
      if (result.breakpoints[MAX_WIDTH_1024PX]) {
        this.modeValue = 'over';
      } else if (result.breakpoints[MIN_WIDTH_1025PX]) {
        this.modeValue = 'side';
      }
    });
    this.breakpointObserver.observe([MIN_WIDTH_1400PX, MAX_WIDTH_1399PX]).subscribe((result) => {
      if (result.breakpoints[MAX_WIDTH_1399PX]) {
        this.status = 'false';
      } else if (result.breakpoints[MIN_WIDTH_1400PX]) {
        this.status = 'true';
      }
    });
  }

  // public method
  tabChanged(index: number) {
    this.selectedTabIndex = index;
  }

  onClick() {
    this.mailListHight = !this.mailListHight;
  }

  composeMail() {
    this.dialog.open(ComposeMailComponent, {
      width: '500px'
    });
  }
}
