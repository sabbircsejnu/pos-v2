// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

// Type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'price-1',
        loadComponent: () => import('./pricing/pricing.component').then((c) => c.PricingComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'price-2',
        loadComponent: () => import('./price-two/price-two.component').then((c) => c.PriceTwoComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class PriceRoutingModule {}
