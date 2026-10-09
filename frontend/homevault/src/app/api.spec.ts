import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Api, failureMessage, safeReturnUrl } from './api';
import { authenticated } from './routes';

describe('HTTP and session boundary', () => {
  let api: Api; let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })),
      provideHttpClientTesting(), provideRouter([]),
    ] });
    api = TestBed.inject(Api); http = TestBed.inject(HttpTestingController);
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; Path=/';
  });
  afterEach(() => { http.verify(); document.cookie = 'XSRF-TOKEN=; Max-Age=0; Path=/'; });

  it('bootstraps antiforgery before creating a Vault with only the approved DTO', () => {
    let created = '';
    api.createVault('Household', 'household').subscribe(value => created = value.id);
    http.expectNone('/api/v1/vaults');
    const bootstrap = http.expectOne('/auth/antiforgery');
    expect(bootstrap.request.method).toBe('GET');
    document.cookie = 'XSRF-TOKEN=fictional-xsrf; Path=/'; bootstrap.flush(null);
    const write = http.expectOne('/api/v1/vaults');
    expect(write.request.headers.get('X-XSRF-TOKEN')).toBe('fictional-xsrf');
    expect(write.request.body).toEqual({ name: 'Household', type: 'household' });
    write.flush({ id: 'vault', name: 'Household', type: 'household' }, { status: 201, statusText: 'Created' });
    expect(created).toBe('vault');
  });

  it('does not send a mutation when antiforgery bootstrap fails', () => {
    const rejected = vi.fn(); api.login('fictional@example.invalid', 'fictional password').subscribe({ error: rejected });
    http.expectOne('/auth/antiforgery').flush(null, { status: 503, statusText: 'Unavailable' });
    http.expectNone('/auth/login'); expect(rejected).toHaveBeenCalledOnce();
  });

  it('refreshes the token after login and never retries a failed mutation', () => {
    api.login('fictional@example.invalid', 'fictional password').subscribe();
    http.expectOne('/auth/antiforgery').flush(null);
    http.expectOne('/auth/login').flush(null);
    expect(api.signedIn()).toBe(true);
    const rejected = vi.fn(); api.addAsset('vault', 'Car').subscribe({ error: rejected });
    document.cookie = 'XSRF-TOKEN=fictional-new-token; Path=/';
    http.expectOne('/auth/antiforgery').flush(null);
    const write = http.expectOne('/api/v1/vaults/vault/assets');
    expect(write.request.body).toEqual({ name: 'Car' });
    expect(write.request.headers.get('X-XSRF-TOKEN')).toBe('fictional-new-token');
    write.flush({ detail: 'private provider detail' }, { status: 500, statusText: 'Failure' });
    expect(rejected).toHaveBeenCalledOnce(); http.expectNone('/api/v1/vaults/vault/assets');
  });

  it('uses a fresh GET for every Asset inspection without client persistence', () => {
    api.asset('one').subscribe(); api.asset('one').subscribe();
    const requests = http.match('/api/v1/assets/one'); expect(requests).toHaveLength(2);
    requests.forEach(request => { expect(request.request.method).toBe('GET'); request.flush({ id: 'one', name: 'Car', vaultId: 'vault' }); });
  });

  it('clears confirmation metadata when the authenticated account changes or expires', () => {
    api.session().subscribe(); http.expectOne('/auth/session').flush({ actorId: 'first' });
    api.createdVault.set({ id: 'vault', name: 'Private title', type: 'personal' });
    api.session().subscribe(); http.expectOne('/auth/session').flush({ actorId: 'second' });
    expect(api.createdVault()).toBeNull();
    api.session().subscribe(); http.expectOne('/auth/session').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(api.signedIn()).toBe(false);
  });

  it('routes an anonymous saved-link visit to sign-in without loading its record', () => {
    let result: boolean | UrlTree | undefined;
    const guard = TestBed.runInInjectionContext(() => authenticated({} as ActivatedRouteSnapshot, { url: '/assets/11111111-1111-1111-1111-111111111111' } as RouterStateSnapshot)) as Observable<boolean | UrlTree>;
    guard.subscribe(value => result = value);
    http.expectOne('/auth/session').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toContain('/sign-in?returnUrl=');
    http.expectNone(request => request.url.startsWith('/api/'));
  });

  it('uses the cookie logout contract and clears ephemeral confirmation state', () => {
    api.createdVault.set({ id: 'vault', name: 'Title', type: 'personal' }); api.signedIn.set(true);
    api.logout().subscribe(); http.expectOne('/auth/antiforgery').flush(null);
    const request = http.expectOne('/auth/logout'); expect(request.request.method).toBe('POST'); request.flush(null);
    expect(api.signedIn()).toBe(false); expect(api.createdVault()).toBeNull();
  });
});

describe('Safe messages and navigation', () => {
  it.each([0, 400, 401, 403, 404, 409, 413, 429, 500, 503])('does not expose a server body for status %s', (status) => {
    const message = failureMessage(new HttpErrorResponse({ status, error: { code: 'untrusted', detail: 'SECRET provider trace', title: 'SECRET' } }));
    expect(message).not.toContain('SECRET'); expect(message.length).toBeGreaterThan(10);
  });
  it('distinguishes archived writes while missing and inaccessible records stay identical', () => {
    expect(failureMessage(new HttpErrorResponse({ status: 409, error: { code: 'vault_archived' } }))).toContain('archived');
    expect(failureMessage(new HttpErrorResponse({ status: 404, error: 'missing' }))).toBe(failureMessage(new HttpErrorResponse({ status: 404, error: 'denied' })));
  });
  it.each(['https://example.invalid', '//example.invalid', '/\\example.invalid', '/sign-in', '/assets/not-an-id', '/vaults/new?redirect=https://example.invalid'])('rejects an unsafe or unsupported return URL: %s', value => {
    expect(safeReturnUrl(value)).toBe('/');
  });
  it('allows a saved Asset link after login', () => {
    const path = '/assets/11111111-1111-1111-1111-111111111111'; expect(safeReturnUrl(path)).toBe(path);
  });
});
