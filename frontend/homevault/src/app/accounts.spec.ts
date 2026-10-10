import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { Api } from './api';
import { RedemptionPage } from './accounts';

describe('Account redemption privacy', () => {
  it('sends only the redemption DTO and clears private inputs on failure', () => {
    const pending = new Subject<void>(); const mutate = vi.fn(() => pending);
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { data: { recovery: true } } } }, { provide: Api, useValue: { mutate } }] });
    const fixture = TestBed.createComponent(RedemptionPage); const page = fixture.componentInstance;
    page.form.setValue({ login: 'fictional@example.invalid', secret: 'fictional-code', password: 'fictional-password', confirm: 'fictional-password' });
    page.submit(); page.submit();
    expect(mutate).toHaveBeenCalledExactlyOnceWith('POST', '/auth/recovery/redeem', { login: 'fictional@example.invalid', secret: 'fictional-code', password: 'fictional-password' });
    pending.error({ status: 400, detail: 'private exception' });
    expect(page.form.controls.secret.value).toBe(''); expect(page.form.controls.password.value).toBe('');
    expect(page.error()).not.toContain('private exception'); fixture.destroy();
  });
});
