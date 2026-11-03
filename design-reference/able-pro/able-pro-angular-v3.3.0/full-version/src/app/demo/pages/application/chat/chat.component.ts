// angular import
import { AfterViewInit, Component, inject, OnInit } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { CommonModule } from '@angular/common';

// angular material
import { MatDrawerMode } from '@angular/material/sidenav';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { chatPersonData } from 'src/app/demo/data/chat';
import { chatsHistory } from 'src/app/demo/data/chat-history';
import { AbleProConfig } from 'src/app/app-config';
import { NgxScrollbar } from 'src/app/@theme/components/ngx-scrollbar/ngx-scrollbar';

// type
import { chatHistory, chatPersonType } from 'src/app/@theme/types/chat-type';

//const
import { MIN_WIDTH_1025PX, MAX_WIDTH_1024PX, MIN_WIDTH_1400PX, MAX_WIDTH_1399PX } from 'src/app/@theme/const';

@Component({
  selector: 'app-chat',
  imports: [SharedModule, CommonModule, MatExpansionModule, MatSlideToggleModule, NgxScrollbar],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss'
})
export class ChatComponent implements OnInit, AfterViewInit {
  // breakpoint observer
  private breakpointObserver = inject(BreakpointObserver);

  // public props
  modeValue: MatDrawerMode = 'side';
  infoStatus: string = 'false';
  personStatus: string = 'true';
  userStatus: string = 'active';
  rtlMode: boolean = false;

  chatPersonList: chatPersonType[] = chatPersonData; // chat person list Data
  chatHistory: chatHistory[] = chatsHistory; // chat history Data
  selectedUser: chatPersonType; // sidebar on click to selected user data
  selectedUserChatHistory: chatHistory[] = []; // sidebar on click to selected user chat history
  selectedPersonId!: number; // sidebar on click to selected user id

  message: string = ''; // send new message
  errorMessage: string = ''; // error message

  // list to search any person
  searchTerm: string = '';

  // life cycle hook
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
        this.personStatus = 'false';
      } else if (result.breakpoints[MIN_WIDTH_1400PX]) {
        this.personStatus = 'true';
      }
    });

    this.selectedUser = this.chatPersonList[0];
    this.selectedUserChatHistory = this.chatHistory.filter((x) => x.from === 'Alene' || x.to === 'Alene');
    this.selectedPersonId = this.selectedUser.id;
  }

  ngAfterViewInit() {
    this.rtlMode = AbleProConfig.isRtlLayout;
  }

  // public method
  chatPerson(id: number) {
    // sidebar on click to selected user
    this.selectedUser = this.chatPersonList.filter((x) => x.id === id)[0];

    // sidebar on click to selected user chat history
    this.selectedUserChatHistory = this.chatHistory.filter(
      (message) => message.from === this.selectedUser.name || message.to === this.selectedUser.name
    );
    this.selectedPersonId = this.selectedUser.id;
  }

  // send new message
  sendNewMessage(name: string) {
    if (this.message.trim() !== '') {
      const newMessage = {
        id: Math.max(...this.chatHistory.map((message) => message.id), 0) + 1, // You need to implement a function to get the next available ID
        from: 'User1',
        to: name,
        text: this.message,
        time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      };
      this.selectedUserChatHistory.push(newMessage);
      this.message = '';
      this.errorMessage = '';
    } else {
      this.errorMessage = 'Please Enter Any Message.';
    }
  }

  // user status
  setStatus(status: string) {
    this.userStatus = status;
  }

  // filter person form list
  filterPerson(searchTerm: string): void {
    this.chatPersonList = chatPersonData.filter(
      (person) =>
        person.name.toLowerCase().includes(searchTerm.toLowerCase()) || person.status.toLowerCase().includes(searchTerm.toLowerCase())
    );
  }

  footer_icon = [
    {
      icon: 'ti ti-mood-smile'
    },
    {
      icon: 'ti ti-paperclip'
    },
    {
      icon: 'ti ti-photo-scan'
    },
    {
      icon: 'ti ti-microphone'
    }
  ];

  cards = [
    {
      title: 'All File',
      amount: '231',
      background: 'bg-primary-100',
      text_color: 'text-primary-500 ti ti-folder-open'
    },
    {
      title: 'All Link',
      amount: '250',
      background: 'bg-accent-100',
      text_color: 'text-accent-500 ti ti-link'
    }
  ];

  file = [
    {
      background: 'bg-success-50 text-success-500',
      icon: 'ti ti-clipboard-text',
      title: 'Document',
      text: '123 files, 193MB'
    },
    {
      background: 'bg-warning-50 text-warning-500',
      icon: 'ti ti-photo',
      title: 'Photos',
      text: '53 files, 321MB'
    },
    {
      background: 'bg-primary-50 text-primary-500',
      icon: 'ti ti-file-chart',
      title: 'Other',
      text: '49 files, 193MB'
    }
  ];
}
