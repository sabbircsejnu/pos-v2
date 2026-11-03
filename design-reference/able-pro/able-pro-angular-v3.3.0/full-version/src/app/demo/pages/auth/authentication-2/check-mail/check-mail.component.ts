// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-check-mail',
  imports: [CommonModule, SharedModule, RouterModule],
  templateUrl: './check-mail.component.html',
  styleUrls: ['./check-mail.component.scss', '../authentication-2.scss', '../../authentication.scss']
})
export class CheckMailComponent {}
