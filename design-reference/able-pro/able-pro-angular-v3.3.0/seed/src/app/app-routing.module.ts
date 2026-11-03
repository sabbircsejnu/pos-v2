// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

// project import
import { AdminComponent } from './demo/layout/admin';
import { EmptyComponent } from './demo/layout/empty/empty.component';
import { GuestComponent } from './demo/layout/front/guest.component';
import { AuthGuardChild } from './@theme/helpers/auth.guard';
import { Role } from './@theme/types/role';

const routes: Routes = [
  {
    path: '',
    component: EmptyComponent,
    children: [
      {
        path: '',
        loadComponent: () => import('./demo/pages/auth/authentication-1/login/login.component').then((c) => c.LoginComponent)
      },
      {
        path: '',
        loadChildren: () => import('./demo/pages/auth/authentication-1/authentication-1.module').then((e) => e.Authentication1Module)
      },
      {
        path: 'maintenance',
        loadChildren: () => import('./demo/pages/maintenance/maintenance.module').then((m) => m.MaintenanceModule)
      }
    ]
  },
  {
    path: '',
    component: GuestComponent,
    children: [
      {
        path: 'register',
        loadComponent: () => import('./demo/pages/auth/authentication-1/register/register.component').then((c) => c.RegisterComponent)
      },
      {
        path: 'forgot-password',
        loadComponent: () =>
          import('./demo/pages/auth/authentication-1/forgot-password/forgot-password.component').then((c) => c.ForgotPasswordComponent)
      },
      {
        path: 'contact-us',
        loadComponent: () => import('./demo/pages/contact-us/contact-us.component').then((c) => c.ContactUsComponent)
      },
      {
        path: 'components',
        loadChildren: () => import('src/app/demo/layout/component/component.module').then((m) => m.ComponentModule)
      }
    ]
  },
  {
    path: '',
    component: AdminComponent,
    canActivateChild: [AuthGuardChild],
    children: [
      {
        path: 'sample-page',
        loadComponent: () => import('./demo/pages/other/sample-page/sample-page.component').then((c) => c.SamplePageComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  },

  {
    path: '**',
    loadComponent: () => import('./demo/pages/maintenance/error/error.component').then((c) => c.ErrorComponent)
  }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule {}
