// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

//type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'payment',
        loadComponent: () => import('./payment/payment.component').then((c) => c.PaymentComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'price',
        loadComponent: () => import('./pricing/pricing.component').then((c) => c.PricingComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'notification',
        loadComponent: () => import('./notification/notification.component').then((c) => c.NotificationComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class SettingRoutingModule {}
