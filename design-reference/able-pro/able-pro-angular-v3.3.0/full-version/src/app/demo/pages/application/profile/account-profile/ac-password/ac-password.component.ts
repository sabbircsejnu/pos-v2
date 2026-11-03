// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-ac-password',
  imports: [CommonModule, SharedModule],
  templateUrl: './ac-password.component.html',
  styleUrls: ['../account-profile.scss', './ac-password.component.scss']
})
export class AcPasswordComponent {
  // public props
  hide = true;
  newHide = true;
  conHide = true;
  password: string;
  passwordStrength: string;

  // public method
  checkPasswordStrength() {
    const passwordStrengthRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$/;
    const password = this.password;

    if (passwordStrengthRegex.test(password)) {
      this.passwordStrength = 'Strong';
    } else if (password.length >= 8) {
      this.passwordStrength = 'Medium';
    } else {
      this.passwordStrength = 'Weak';
    }
  }

  password_type = [
    {
      title: ' At least 8 characters'
    },
    {
      title: ' At least 1 lower letter (a-z)'
    },
    {
      title: ' At least 1 uppercase letter(A-Z)'
    },
    {
      title: ' At least 1 number (0-9)'
    },
    {
      title: ' At least 1 special characters'
    }
  ];
}
