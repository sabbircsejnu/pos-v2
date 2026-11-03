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
        path: 'account',
        loadChildren: () => import('./account-profile/account-profile.module').then((c) => c.AccountProfileModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'social',
        loadComponent: () => import('./social-profile/social-profile.component').then((c) => c.SocialProfileComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'user',
        loadChildren: () => import('./user-profiles/user-profiles.module').then((m) => m.UserProfilesModule),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class ProfileRoutingModule {}
