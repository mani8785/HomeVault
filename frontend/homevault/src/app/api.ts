import { inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { catchError, map, Observable, of, switchMap, tap, throwError } from 'rxjs';

export type VaultType = 'personal' | 'household' | 'organization';
export interface Vault { id: string; name: string; type: VaultType }
export interface Asset { id: string; vaultId: string; name: string }
export type Role = 'owner' | 'administrator' | 'editor' | 'viewer';
export interface LibraryVault extends Vault { status: 'active' | 'archived'; role: Role }
export interface Page<T> { items: T[]; hasMore: boolean }

/** HTTP transport only. The API remains authoritative for identities, validation and access. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private actor = '';
  readonly signedIn = signal(false);
  readonly actorId = signal('');
  // Ephemeral confirmation only: never write record payloads to browser storage or route history.
  readonly createdVault = signal<Vault | null>(null);

  session(): Observable<boolean> {
    return this.http.get<{ actorId: string }>('/auth/session').pipe(
      map((session) => {
        if (this.actor !== session.actorId) this.createdVault.set(null);
        this.actor = session.actorId; this.actorId.set(session.actorId); return true;
      }),
      catchError((error: unknown) => error instanceof HttpErrorResponse && error.status === 401 ? of(false) : throwError(() => error)),
      tap((authenticated) => { this.signedIn.set(authenticated); if (!authenticated) { this.actor = ''; this.actorId.set(''); this.createdVault.set(null); } }),
    );
  }
  login(login: string, password: string): Observable<void> {
    this.createdVault.set(null); this.actor = '';
    return this.post<void>('/auth/login', { login, password }).pipe(tap(() => this.signedIn.set(true)));
  }
  logout(): Observable<void> {
    return this.post<void>('/auth/logout', {}).pipe(tap(() => this.clearSession()));
  }
  clearSession(): void { this.signedIn.set(false); this.actor = ''; this.actorId.set(''); this.createdVault.set(null); }
  createVault(name: string, type: VaultType): Observable<Vault> {
    return this.post<Vault>('/api/v1/vaults', { name, type }).pipe(tap((vault) => this.createdVault.set(vault)));
  }
  addAsset(vaultId: string, name: string): Observable<Asset> {
    return this.post<Asset>(`/api/v1/vaults/${encodeURIComponent(vaultId)}/assets`, { name });
  }
  asset(id: string): Observable<Asset> { return this.http.get<Asset>(`/api/v1/assets/${encodeURIComponent(id)}`); }

  /** Bounded same-origin reads. Callers supply only application-owned route prefixes. */
  read<T>(path: string): Observable<T> { return this.http.get<T>(path); }

  /** Every mutation bootstraps antiforgery; null means an intentionally empty HTTP body. */
  mutate<T>(method: 'POST' | 'PUT' | 'DELETE', path: string, body: unknown = null): Observable<T> {
    return this.http.get<void>('/auth/antiforgery').pipe(switchMap(() => this.http.request<T>(method, path, { body })));
  }

  private post<T>(url: string, body: unknown): Observable<T> {
    // Login changes the antiforgery identity. Bootstrap before each mutation; never retry a POST.
    return this.mutate<T>('POST', url, body);
  }
}

/** Translate only known statuses/codes; never display a server body, exception or submitted value. */
export function failureMessage(error: unknown, login = false): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
  switch (error.status) {
    case 0: return 'HomeVault could not reach the server. Check that it is running, then try again.';
    case 400: return 'Check the form and try again. If the problem continues, reload the page.';
    case 401: return login ? 'Sign-in was not successful. Check your details and try again.' : 'Your session has ended. Sign in again to continue.';
    case 403: return 'You do not have permission to make this change.';
    case 404: return 'This record is unavailable. It may not exist, or you may not have access.';
    case 409: return error.error?.code === 'vault_archived' ? 'This Vault is archived. New Assets cannot be added.' : 'This change could not be completed because the record has changed.';
    case 429: return 'Too many requests. Please wait a minute before trying again.';
    default: return 'HomeVault could not complete the request. Please try again later.';
  }
}

export const validId = (value: string): boolean => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';

export function safeReturnUrl(value: string | null): string {
  return value !== null && /^\/(?:vaults\/new|vaults\/[0-9a-f-]{36}\/(?:ready|assets\/new)|assets\/[0-9a-f-]{36})$/i.test(value) ? value : '/';
}
