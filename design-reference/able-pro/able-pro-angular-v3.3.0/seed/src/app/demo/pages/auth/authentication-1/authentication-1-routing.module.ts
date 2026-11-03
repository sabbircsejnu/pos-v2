// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

// type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'login',
        loadComponent: () => import('./login/login.component').then((c) => c.LoginComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'register',
        loadComponent: () => import('./register/register.component').then((c) => c.RegisterComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'forgot-password',
        loadComponent: () => import('./forgot-password/forgot-password.component').then((c) => c.ForgotPasswordComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'reset-password',
        loadComponent: () => import('./reset-password/reset-password.component').then((c) => c.ResetPasswordComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'check-mail',
        loadComponent: () => import('./check-mail/check-mail.component').then((c) => c.CheckMailComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'code-verify',
        loadComponent: () => import('./code-verification/code-verification.component').then((c) => c.CodeVerificationComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class Authentication1RoutingModule {}
