import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { Api, Asset, failureMessage, safeReturnUrl, validId, VaultType } from './api';
import { Inspector } from './inspector';

@Component({
  imports: [RouterLink],
  template: `
    <section class="welcome">
      <p class="eyebrow">YOUR PERSONAL RECORDS</p>
      <h1>Make room for<br>what matters.</h1>
      <p class="lead">Bring your important things together, one record at a time. Start with a Vault, then add your first Asset.</p>
      <a class="button" routerLink="/vaults/new">Create a Vault <span aria-hidden="true">↗</span></a>
      <div class="journey" aria-label="Your first steps">
        <article><span class="step">01</span><h2>Create a Vault</h2><p>Choose a space for yourself, your household or your organization.</p></article>
        <article><span class="step">02</span><h2>Add an Asset</h2><p>Give something important a record of its own.</p></article>
        <article><span class="step">03</span><h2>Keep its link</h2><p>Bookmark the Asset page to return to it later.</p></article>
      </div>
      <p class="quiet">Already have an Asset? Open its saved link. Browsing and search will come in a later step.</p>
    </section>
  `,
})
export class HomePage {}

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section class="form-page"><p class="eyebrow">WELCOME BACK</p><h1>Sign in to HomeVault</h1>
      <p class="lead">Your Vaults stay yours. Sign in with your HomeVault account to continue.</p>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate [attr.aria-busy]="busy()">
        @if (submitted() && form.invalid) {
          <div class="error-summary" role="alert"><h2>Check your details</h2><ul>
            @if (form.controls.login.invalid) { <li><a href="#login" (click)="$event.preventDefault(); loginInput.focus()">Enter your email address.</a></li> }
            @if (form.controls.password.invalid) { <li><a href="#password" (click)="$event.preventDefault(); passwordInput.focus()">Enter your password.</a></li> }
          </ul></div>
        }
        @if (error()) { <p class="error-summary" role="alert">{{ error() }}</p> }
        <label for="login">Email address</label>
        <input #loginInput id="login" type="email" formControlName="login" autocomplete="username" maxlength="256" [attr.aria-invalid]="submitted() && form.controls.login.invalid" aria-describedby="login-error">
        <p id="login-error" class="field-error">@if (submitted() && form.controls.login.invalid) { Enter your email address. }</p>
        <label for="password">Password</label>
        <input #passwordInput id="password" type="password" formControlName="password" autocomplete="current-password" maxlength="128" [attr.aria-invalid]="submitted() && form.controls.password.invalid" aria-describedby="password-error">
        <p id="password-error" class="field-error">@if (submitted() && form.controls.password.invalid) { Enter your password. }</p>
        <button class="button" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
      </form>
      <p class="quiet">Accounts are invitation-only. Ask your HomeVault operator if you need an account or help recovering access.</p>
      <p><a routerLink="/invitation">Accept an invitation</a> · <a routerLink="/recovery">Use a recovery code</a></p>
    </section>
  `,
})
export class SignInPage {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly form = new FormGroup({
    login: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(128)] }),
  });
  readonly busy = signal(false); readonly submitted = signal(false); readonly error = signal('');
  submit(): void {
    if (this.busy()) return;
    this.submitted.set(true); this.error.set('');
    if (this.form.invalid) return;
    this.busy.set(true);
    this.api.login(this.form.controls.login.value, this.form.controls.password.value).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.submitted.set(false); this.form.controls.password.reset(); void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'))); },
      error: (error: unknown) => { this.submitted.set(false); this.form.controls.password.reset(); this.error.set(failureMessage(error, true)); },
    });
  }
}

const nameControl = (): FormControl<string> => new FormControl('', {
  nonNullable: true,
  validators: [(control) => typeof control.value === 'string' && control.value.trim().length > 0 ? null : { required: true }],
});

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section class="form-page"><a class="back-link" routerLink="/">← Home</a><p class="eyebrow">STEP 01 / YOUR SPACE</p>
      <h1>Create a Vault</h1><p class="lead">A Vault brings related records together. You will be its first Owner.</p>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate [attr.aria-busy]="busy()">
        @if (submitted() && form.invalid) { <div class="error-summary" role="alert"><h2>Check your Vault</h2><a href="#vault-name" (click)="$event.preventDefault(); vaultName.focus()">Enter a Vault name.</a></div> }
        @if (error()) { <div class="error-summary" role="alert"><p>{{ error() }}</p><a routerLink="/sign-in" [queryParams]="{returnUrl: '/vaults/new'}">Sign in if your session has ended</a></div> }
        <label for="vault-name">Vault name</label><input #vaultName id="vault-name" formControlName="name" autocomplete="off" aria-describedby="vault-name-help vault-name-error" [attr.aria-invalid]="submitted() && form.controls.name.invalid">
        <p id="vault-name-help" class="hint">For example, My household. Keep secrets out of names.</p>
        <p id="vault-name-error" class="field-error">@if (submitted() && form.controls.name.invalid) { Enter a Vault name. }</p>
        <label for="vault-type">Who is this Vault for?</label>
        <select id="vault-type" formControlName="type"><option value="personal">Just me</option><option value="household">My household</option><option value="organization">An organization</option></select>
        <button class="button" type="submit" [disabled]="busy()">{{ busy() ? 'Creating…' : 'Create Vault' }}</button>
      </form>
    </section>
  `,
})
export class CreateVaultPage {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(Api); private readonly router = inject(Router);
  readonly form = new FormGroup({ name: nameControl(), type: new FormControl<VaultType>('personal', { nonNullable: true }) });
  readonly busy = signal(false); readonly submitted = signal(false); readonly error = signal('');
  submit(): void {
    if (this.busy()) return;
    this.submitted.set(true); this.error.set(''); if (this.form.invalid) return;
    this.busy.set(true);
    this.api.createVault(this.form.controls.name.value, this.form.controls.type.value).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false))).subscribe({
      next: (vault) => { void this.router.navigate(['/vaults', vault.id, 'ready']); },
      error: (error: unknown) => this.error.set(failureMessage(error)),
    });
  }
}

@Component({
  imports: [RouterLink],
  template: `
    <section class="form-page"><p class="eyebrow">YOUR NEXT STEP</p>
      <h1>{{ vault() ? 'Your Vault is ready.' : 'Continue with this Vault' }}</h1>
      @if (vault(); as created) { <p class="record-name">{{ created.name }}</p> }
      <p class="lead">Add a record for something you want to keep track of.</p>
      <a class="button" [routerLink]="['/vaults', id, 'assets', 'new']">Add an Asset <span aria-hidden="true">↗</span></a>
      <p class="quiet">Current access is checked when you add an Asset. Keep the next Asset page's link to return to its record.</p>
      <a class="back-link" routerLink="/">Back to home</a>
    </section>
  `,
})
export class VaultReadyPage {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  get id(): string { return this.route.snapshot.paramMap.get('vaultId') ?? ''; }
  vault() { const value = this.api.createdVault(); return value?.id === this.id ? value : null; }
}

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section class="form-page"><a class="back-link" routerLink="/">← Home</a><p class="eyebrow">STEP 02 / YOUR FIRST RECORD</p>
      <h1>Add an Asset</h1><p class="lead">Give something important a place in this Vault.</p>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate [attr.aria-busy]="busy()">
        @if (submitted() && form.invalid) { <div class="error-summary" role="alert"><h2>Check your Asset</h2><a href="#asset-name" (click)="$event.preventDefault(); assetName.focus()">Enter an Asset name.</a></div> }
        @if (error()) { <div class="error-summary" role="alert"><p>{{ error() }}</p><a routerLink="/sign-in" [queryParams]="{returnUrl: returnUrl}">Sign in if your session has ended</a></div> }
        <label for="asset-name">Asset name</label><input #assetName id="asset-name" formControlName="name" autocomplete="off" aria-describedby="asset-name-help asset-name-error" [attr.aria-invalid]="submitted() && form.controls.name.invalid">
        <p id="asset-name-help" class="hint">For example, Family car or Home insurance. Do not put passwords or other secrets here.</p>
        <p id="asset-name-error" class="field-error">@if (submitted() && form.controls.name.invalid) { Enter an Asset name. }</p>
        <button class="button" type="submit" [disabled]="busy()">{{ busy() ? 'Adding…' : 'Add Asset' }}</button>
      </form>
    </section>
  `,
})
export class AddAssetPage {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(Api); private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private pending?: Subscription;
  get id(): string { return this.route.snapshot.paramMap.get('vaultId') ?? ''; }
  get returnUrl(): string { return `/vaults/${this.id}/assets/new`; }
  readonly form = new FormGroup({ name: nameControl() });
  readonly busy = signal(false); readonly submitted = signal(false); readonly error = signal('');
  constructor() {
    this.destroyRef.onDestroy(() => this.pending?.unsubscribe());
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(() => {
      this.pending?.unsubscribe(); this.form.reset(); this.submitted.set(false); this.error.set('');
    });
  }
  submit(): void {
    if (this.busy()) return;
    this.submitted.set(true); this.error.set(''); if (this.form.invalid) return;
    if (!validId(this.id)) { this.error.set('This Vault reference is unavailable.'); return; }
    this.busy.set(true);
    this.pending = this.api.addAsset(this.id, this.form.controls.name.value).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (asset) => { void this.router.navigate(['/assets', asset.id]); },
      error: (error: unknown) => this.error.set(failureMessage(error)),
    });
  }
}

@Component({
  imports: [RouterLink, Inspector],
  template: `
    <section class="asset-detail-page"><a class="back-link" routerLink="/">← My library</a><p class="eyebrow">ASSET RECORD</p>
      @if (loading()) { <h1>Opening your Asset…</h1><p role="status">Loading the current record.</p> }
      @if (asset(); as record) {
        <h1 class="record-name">{{ record.name }}</h1><p class="status"><span aria-hidden="true">✓</span> Saved in your Vault</p>
        <div class="info-card"><h2>A record you can return to</h2><p>Bookmark this page to find your Asset again. The latest record is loaded whenever you open or refresh this link.</p></div>
        <a class="button secondary" [routerLink]="['/vaults', record.vaultId, 'assets', 'new']">Add another Asset</a>
        <hv-inspector [asset]="record" (unavailable)="asset.set(null); error.set('Access changed. Return to your library and refresh.')" />
      }
      @if (error()) { <h1>Asset unavailable</h1><p class="error-summary" role="alert">{{ error() }}</p><button class="button secondary" (click)="load()" [disabled]="loading()">Try again</button> }
    </section>
  `,
})
export class AssetPage {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private pending?: Subscription;
  readonly asset = signal<Asset | null>(null); readonly loading = signal(false); readonly error = signal('');
  constructor() {
    inject(DestroyRef).onDestroy(() => this.pending?.unsubscribe());
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe(() => this.load());
  }
  load(): void {
    this.pending?.unsubscribe();
    const id = this.route.snapshot.paramMap.get('assetId') ?? '';
    this.asset.set(null); this.error.set('');
    if (!validId(id)) { this.error.set('This record is unavailable.'); return; }
    this.loading.set(true);
    this.pending = this.api.asset(id).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (asset) => this.asset.set(asset), error: (error: unknown) => this.error.set(failureMessage(error)),
    });
  }
}

@Component({ imports: [RouterLink], template: `<section class="form-page"><h1>Page unavailable</h1><p>This link does not match a HomeVault page.</p><a class="button" routerLink="/">Go home</a></section>` })
export class NotFoundPage {}
