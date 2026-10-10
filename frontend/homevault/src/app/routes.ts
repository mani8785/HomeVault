import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { Api } from './api';
import { LibraryPage } from './library';
import { AddAssetPage, AssetPage, CreateVaultPage, HomePage, NotFoundPage, SignInPage, VaultReadyPage } from './pages';

export const authenticated: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(Api).session().pipe(
    map((signedIn) => signedIn || router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } })),
    catchError(() => of(router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } }))),
  );
};

export const routes: Routes = [
  { path: 'sign-in', component: SignInPage, title: 'Sign in · HomeVault' },
  { path: '', component: LibraryPage, canActivate: [authenticated], pathMatch: 'full', title: 'My library · HomeVault' },
  { path: 'vaults/new', component: CreateVaultPage, canActivate: [authenticated], title: 'Create a Vault · HomeVault' },
  { path: 'vaults/:vaultId/ready', component: VaultReadyPage, canActivate: [authenticated], title: 'Your Vault · HomeVault' },
  { path: 'vaults/:vaultId/assets/new', component: AddAssetPage, canActivate: [authenticated], title: 'Add an Asset · HomeVault' },
  { path: 'assets/:assetId', component: AssetPage, canActivate: [authenticated], title: 'Asset · HomeVault' },
  { path: '**', component: NotFoundPage, title: 'Page unavailable · HomeVault' },
];
