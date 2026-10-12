import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs';
import { Api, failureMessage } from './api';

@Component({
  selector: 'hv-app',
  imports: [RouterLink, RouterOutlet],
  template: `
    <a class="skip-link" href="#main" (click)="$event.preventDefault(); focusMain()">Skip to content</a>
    <header class="site-header">
      <a class="brand" routerLink="/" aria-label="HomeVault home"><span class="brand-mark" aria-hidden="true">H</span>HomeVault</a>
      <nav aria-label="Account">
        @if (api.signedIn()) { <a routerLink="/account">Account</a> }
        @if (api.signedIn()) { <button class="text-button" (click)="logout()" [disabled]="busy()">{{ busy() ? 'Signing out…' : 'Sign out' }}</button> }
      </nav>
    </header>
    @if (error()) { <p class="shell-error" role="alert">{{ error() }}</p> }
    <main #main id="main" tabindex="-1"><router-outlet (activate)="focusMain()" /></main>
    <footer>HomeVault <span aria-hidden="true">·</span> A place for what matters.</footer>
  `,
})
export class App {
  readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly main = viewChild<ElementRef<HTMLElement>>('main');
  readonly busy = signal(false);
  readonly error = signal('');
  focusMain(): void { setTimeout(() => this.main()?.nativeElement.focus()); }
  logout(): void {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    this.api.logout().pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { void this.router.navigateByUrl('/sign-in'); },
      error: (error: unknown) => this.error.set(failureMessage(error)),
    });
  }
}
