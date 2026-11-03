// angular import
import { ChangeDetectorRef, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';

import { form, Field } from '@angular/forms/signals';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { AuthenticationService } from 'src/app/@theme/services/authentication.service';
import { DASHBOARD_PATH } from 'src/app/app-config';
import { BuyNowLinkService } from 'src/app/@theme/services/buy-now-link.service';

interface Roles {
  name: string;
  email: string;
  password: string;
  role: string;
}

interface LoginData {
  email: string;
  password: string;
}

@Component({
  selector: 'app-login',
  imports: [CommonModule, SharedModule, RouterModule, Field],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss', '../authentication-1.scss', '../../authentication.scss']
})
export class LoginComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  authenticationService = inject(AuthenticationService);
  buyNowLinkService = inject(BuyNowLinkService);
  private cd = inject(ChangeDetectorRef);

  // public props
  hide = true;
  loading = false;
  submitted = false;
  returnUrl: string;
  error = '';

  // Signal holding the form data
  private readonly loginData = signal<LoginData>({
    email: '',
    password: ''
  });

  // Create the signal form based on loginData signal
  loginForm = form(this.loginData);

  ngOnInit() {
    this.loginData.set({ email: this.selectedRole.email, password: this.selectedRole.password });

    if (window.location.pathname !== '/authentication-1/login') {
      if (this.authenticationService.currentUserValue) {
        this.router.navigate([DASHBOARD_PATH], {
          queryParams: this.buyNowLinkService.getQueryParams
        });
      }
    }

    this.returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/';
  }

  // convenience getter for easy access to form fields
  get isFormValid() {
    const val = this.loginForm().value();
    return val.email.trim() !== '' && val.password.trim() !== '';
  }

  onSubmit() {
    this.submitted = true;

    if (!this.isFormValid) {
      return;
    }

    this.loading = true;
    this.error = '';

    const { email, password } = this.loginForm().value();

    this.authenticationService.login(email, password).subscribe({
      next: () => {
        this.router.navigate([DASHBOARD_PATH]);
      },
      error: (error) => {
        this.error = error;
        this.loading = false;
        this.cd.detectChanges();
      }
    });
  }

  roles: Roles[] = [
    {
      name: 'Admin',
      email: 'admin@gmail.com',
      password: 'Admin@123',
      role: 'Admin'
    },
    {
      name: 'User',
      email: 'user@gmail.com',
      password: 'User@123',
      role: 'User'
    }
  ];

  // Default to the first role
  selectedRole = this.roles[0];

  onSelectRole(role: Roles) {
    this.selectedRole = role;
    this.loginData.set({ email: role.email, password: role.password });
  }
}
