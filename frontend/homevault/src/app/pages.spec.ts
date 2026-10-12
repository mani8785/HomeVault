import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { Api, Asset, Vault } from './api';
import { AddAssetPage, AssetPage, CreateVaultPage, SignInPage } from './pages';

const id = '11111111-1111-1111-1111-111111111111';
const secondId = '22222222-2222-2222-2222-222222222222';

describe('Vault and Asset forms', () => {
  let api: { createVault: ReturnType<typeof vi.fn>; addAsset: ReturnType<typeof vi.fn>; login: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    api = { createVault: vi.fn(), addAsset: vi.fn(), login: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Api, useValue: api }, {
      provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ vaultId: id })), snapshot: { paramMap: convertToParamMap({ vaultId: id }), queryParamMap: convertToParamMap({ returnUrl: `/assets/${id}` }) } },
    }] });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  it('links blank-name errors to the labelled input without sending a POST', () => {
    const fixture = TestBed.createComponent(CreateVaultPage); fixture.detectChanges();
    fixture.componentInstance.form.controls.name.setValue('   '); fixture.componentInstance.submit(); fixture.detectChanges();
    expect(api.createVault).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"] a').getAttribute('href')).toBe('#vault-name');
    expect(fixture.nativeElement.querySelector('input').getAttribute('aria-invalid')).toBe('true');
    expect(fixture.nativeElement.querySelector('label').getAttribute('for')).toBe('vault-name');
    const follow = new MouseEvent('click', { bubbles: true, cancelable: true });
    fixture.nativeElement.querySelector('[role="alert"] a').dispatchEvent(follow);
    expect(follow.defaultPrevented).toBe(true);
    expect(document.activeElement?.id).toBe('vault-name');
  });

  it('blocks duplicate submits until the first response then navigates to confirmation', () => {
    const pending = new Subject<Vault>(); api.createVault.mockReturnValue(pending);
    const fixture = TestBed.createComponent(CreateVaultPage); fixture.detectChanges();
    fixture.componentInstance.form.controls.name.setValue('Household');
    fixture.componentInstance.submit(); fixture.componentInstance.submit(); fixture.detectChanges();
    expect(api.createVault).toHaveBeenCalledExactlyOnceWith('Household', 'personal');
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(true);
    pending.next({ id, name: 'Household', type: 'personal' }); pending.complete();
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/vaults', id, 'ready']);
  });

  it.each([400, 401, 403, 404, 409, 429, 500])('keeps Asset input and shows a safe failure for %s', status => {
    api.addAsset.mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { detail: 'private trace', code: 'vault_archived' } })));
    const fixture = TestBed.createComponent(AddAssetPage); fixture.detectChanges();
    fixture.componentInstance.form.controls.name.setValue('Family car'); fixture.componentInstance.submit(); fixture.detectChanges();
    expect(fixture.componentInstance.form.controls.name.value).toBe('Family car');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).not.toContain('private trace');
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(false);
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });

  it('navigates to a read-only Asset URL after one successful addition', () => {
    api.addAsset.mockReturnValue(of({ id: secondId, vaultId: id, name: 'Car' }));
    const fixture = TestBed.createComponent(AddAssetPage);
    fixture.componentInstance.form.controls.name.setValue('Car'); fixture.componentInstance.submit();
    expect(api.addAsset).toHaveBeenCalledExactlyOnceWith(id, 'Car');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/assets', secondId]);
  });

  it('clears a password after rejected sign-in while retaining the login name', () => {
    api.login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const fixture = TestBed.createComponent(SignInPage);
    fixture.componentInstance.form.setValue({ login: 'fictional@example.invalid', password: 'fictional password' });
    fixture.componentInstance.submit(); fixture.detectChanges();
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
    expect(fixture.componentInstance.form.controls.login.value).toBe('fictional@example.invalid');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('not successful');
  });

  it('clears a draft and cancels late navigation when the target Vault changes', () => {
    const pending = new Subject<Asset>(); api.addAsset.mockReturnValue(pending);
    const fixture = TestBed.createComponent(AddAssetPage);
    fixture.componentInstance.form.controls.name.setValue('First draft'); fixture.componentInstance.submit();
    const route = TestBed.inject(ActivatedRoute);
    Object.assign(route.snapshot, { paramMap: convertToParamMap({ vaultId: secondId }) });
    (route.paramMap as BehaviorSubject<unknown>).next(route.snapshot.paramMap);
    pending.next({ id, vaultId: id, name: 'First draft' });
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.name.value).toBe('');
    expect(fixture.componentInstance.busy()).toBe(false);
    api.addAsset.mockReturnValue(of({ id, vaultId: secondId, name: 'New draft' }));
    fixture.componentInstance.form.controls.name.setValue('New draft'); fixture.componentInstance.submit();
    expect(api.addAsset).toHaveBeenLastCalledWith(secondId, 'New draft');
  });

  it('returns to a saved link after login and drops late navigation after leaving a form', () => {
    const login = new Subject<void>(); api.login.mockReturnValue(login);
    const fixture = TestBed.createComponent(SignInPage);
    fixture.componentInstance.form.setValue({ login: 'fictional@example.invalid', password: 'fictional password' });
    fixture.componentInstance.submit(); login.next(); login.complete();
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith(`/assets/${id}`);
    const create = new Subject<Vault>(); api.createVault.mockReturnValue(create);
    const form = TestBed.createComponent(CreateVaultPage); form.componentInstance.form.controls.name.setValue('Name'); form.componentInstance.submit();
    form.destroy(); create.next({ id, name: 'Name', type: 'personal' });
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });
});

describe('Asset detail reload and output encoding', () => {
  it('renders stored HTML-looking text as text, rereads on recreation, and never writes', () => {
    const asset = vi.fn(() => of({ id, vaultId: secondId, name: '<img src=x onerror=alert(1)>' }));
    const params = new BehaviorSubject(convertToParamMap({ assetId: id }));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Api, useValue: { asset } }, {
      provide: ActivatedRoute, useValue: { snapshot: { paramMap: params.value }, paramMap: params },
    }] });
    const first = TestBed.createComponent(AssetPage); first.detectChanges();
    expect(first.nativeElement.querySelector('h1').textContent).toBe('<img src=x onerror=alert(1)>');
    expect(first.nativeElement.querySelector('img')).toBeNull(); first.destroy();
    const reopened = TestBed.createComponent(AssetPage); reopened.detectChanges(); expect(asset).toHaveBeenCalledTimes(2);
  });

  it('cancels the old read when changing Asset routes and does not display stale values', () => {
    const first = new Subject<Asset>(); const second = new Subject<Asset>();
    const asset = vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(second);
    const params = new BehaviorSubject(convertToParamMap({ assetId: id }));
    const route = { snapshot: { paramMap: params.value }, paramMap: params };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Api, useValue: { asset } }, { provide: ActivatedRoute, useValue: route }] });
    const fixture = TestBed.createComponent(AssetPage); fixture.detectChanges();
    route.snapshot.paramMap = convertToParamMap({ assetId: secondId }); params.next(route.snapshot.paramMap);
    first.next({ id, vaultId: id, name: 'stale private title' });
    second.error(new HttpErrorResponse({ status: 404 })); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('stale private title');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('unavailable');
  });
});
