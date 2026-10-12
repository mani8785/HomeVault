import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Api, failureMessage } from './api';

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section class="form-page"><a class="back-link" routerLink="/sign-in">← Sign in</a>
      <h1>{{ recovery ? 'Recover your account' : 'Accept your invitation' }}</h1>
      <p>Use the email address and private {{ recovery ? 'recovery' : 'invitation' }} code supplied by your HomeVault operator. Codes expire and can only be used once.</p>
      @if (done()) { <p role="status">Your password is ready. Sign in to continue.</p><a class="button" routerLink="/sign-in">Sign in</a> } @else {
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (error()) { <p class="error-summary" role="alert">{{ error() }}</p> }
        <label for="enroll-email">Email address</label><input id="enroll-email" type="email" formControlName="login" autocomplete="username" maxlength="256" required>
        <label for="enroll-secret">Private code</label><input id="enroll-secret" type="password" formControlName="secret" autocomplete="off" maxlength="4096" required>
        <label for="enroll-password">New password</label><input id="enroll-password" type="password" formControlName="password" autocomplete="new-password" minlength="15" maxlength="128" aria-describedby="password-rules" required>
        <p id="password-rules" class="hint">Use 15–128 characters. Keep this password private.</p>
        <label for="enroll-confirm">Confirm password</label><input id="enroll-confirm" type="password" formControlName="confirm" autocomplete="new-password" maxlength="128" required>
        <button class="button" type="submit" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Set password' }}</button>
      </form> }
    </section>
  `,
})
export class RedemptionPage {
  private readonly api = inject(Api); private readonly destroy = inject(DestroyRef);
  readonly recovery = inject(ActivatedRoute).snapshot.data['recovery'] === true;
  readonly busy = signal(false); readonly error = signal(''); readonly done = signal(false);
  readonly form = new FormGroup({
    login: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    secret: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(4096)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.minLength(15), Validators.maxLength(128), Validators.required] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  constructor() { this.destroy.onDestroy(() => this.clearPrivate()); }
  private clearPrivate(): void { this.form.controls.secret.reset(); this.form.controls.password.reset(); this.form.controls.confirm.reset(); }
  submit(): void {
    if (this.busy()) return;
    this.error.set('');
    if (this.form.invalid || this.form.controls.password.value !== this.form.controls.confirm.value) { this.error.set('Enter your email, private code and matching passwords of 15–128 characters.'); return; }
    const { login, secret, password } = this.form.getRawValue(); this.busy.set(true);
    this.api.mutate<void>('POST', this.recovery ? '/auth/recovery/redeem' : '/auth/invitations/redeem', { login, secret, password })
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => { this.busy.set(false); this.clearPrivate(); })).subscribe({
        next: () => { this.api.clearSession(); this.done.set(true); },
        error: error => this.error.set(error.status === 400 ? 'The code or account details could not be accepted. Check them or ask your operator for a new code.' : failureMessage(error)),
      });
  }
}

@Component({
  imports: [RouterLink],
  template: `
    <section class="form-page"><a class="back-link" routerLink="/">← My library</a><h1>Account & sessions</h1>
      <h2>Your member ID</h2><p class="record-name">{{ api.actorId() }}</p><p class="hint">Share this ID with a Vault Owner when they need to add you. It is a reference, not a password.</p>
      @if (error()) { <p class="error-summary" role="alert">{{ error() }}</p> }
      @if (confirm()) { <p>Sign out every HomeVault session for this account, including this browser?</p><button class="button" (click)="signOutAll()" [disabled]="busy()">Confirm sign out all</button><button class="text-button" (click)="confirm.set(false)" [disabled]="busy()">Cancel</button> }
      @else { <button class="button secondary" (click)="confirm.set(true)">Sign out all sessions</button> }
      <p class="quiet">Invitation issuance, account disabling and storage/key maintenance are trusted operator terminal tasks.</p>
    </section>
  `,
})
export class AccountPage {
  readonly api = inject(Api); private readonly router = inject(Router); private readonly destroy = inject(DestroyRef);
  readonly confirm = signal(false); readonly busy = signal(false); readonly error = signal('');
  signOutAll(): void {
    if (this.busy() || !this.confirm()) return;
    this.busy.set(true); this.error.set('');
    this.api.mutate<void>('POST', '/auth/sign-out-all').pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.api.clearSession(); void this.router.navigateByUrl('/sign-in'); }, error: error => this.error.set(failureMessage(error)),
    });
  }
}
