import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { Api } from './api';
import { SensitivePanel } from './sensitive-panel';
import { ReminderPanel } from './related-panels';
import { VaultPanel } from './vault-panel';

const id = '11111111-1111-1111-1111-111111111111';

describe('Inspector privacy and mutation contracts', () => {
  it('loads Sensitive metadata without revealing, and rejects a late reveal after Hide', () => {
    const pending = new Subject<{ value: string }>();
    const read = vi.fn(() => of({ attributes: [{ id, name: 'Private label' }] })); const mutate = vi.fn(() => pending);
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: { read, mutate } }] });
    const fixture = TestBed.createComponent(SensitivePanel); fixture.componentRef.setInput('assetId', id); fixture.componentRef.setInput('mayReveal', true); fixture.detectChanges();
    expect(mutate).not.toHaveBeenCalled();
    const panel = fixture.componentInstance; panel.reveal(id);
    expect(mutate).toHaveBeenCalledExactlyOnceWith('POST', `/api/v1/assets/${id}/sensitive-attributes/${id}/read`, null);
    panel.hide(); pending.next({ value: 'fictional private value' }); pending.complete();
    expect(panel.shown()).toBe(''); fixture.destroy();
  });

  it('clears revealed Sensitive plaintext and private input when the window loses focus', () => {
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: { read: () => of({ attributes: [] }), mutate: () => of({ value: 'fictional secret' }) } }] });
    const fixture = TestBed.createComponent(SensitivePanel); fixture.componentRef.setInput('assetId', id); fixture.componentRef.setInput('mayReveal', true); fixture.detectChanges();
    const panel = fixture.componentInstance; panel.reveal(id); expect(panel.shown()).toBe('fictional secret'); panel.value = 'replacement';
    window.dispatchEvent(new Event('blur'));
    expect(panel.shown()).toBe(''); expect(panel.value).toBe(''); fixture.destroy();
  });

  it('does not auto-read Reminder action and preserves exact due precision when only action changes', () => {
    const read = vi.fn((_path: string) => of({ items: [], hasMore: false })); const mutate = vi.fn(() => of(undefined));
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: { read, mutate } }] });
    const fixture = TestBed.createComponent(ReminderPanel); fixture.componentRef.setInput('assetId', id); fixture.componentRef.setInput('vaultId', id); fixture.componentRef.setInput('writable', true); fixture.detectChanges();
    const panel = fixture.componentInstance; const dueAt = '2027-01-01T12:00:00.1234567Z';
    panel.edit({ id, assetId: id, dueAt, status: 'pending' }); panel.action = 'Replacement action'; panel.save();
    expect(read.mock.calls.every(([path]) => !String(path).endsWith('/action'))).toBe(true);
    expect(mutate).toHaveBeenCalledWith('PUT', `/api/v1/vaults/${id}/reminders/${id}`, { action: 'Replacement action', dueAt }); fixture.destroy();
  });

  it('requires archive confirmation and sends an empty body', () => {
    const mutate = vi.fn(() => of(undefined));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Api, useValue: { read: () => of({ items: [], hasMore: false }), mutate } }] });
    const fixture = TestBed.createComponent(VaultPanel); fixture.componentRef.setInput('vault', { id, name: 'Example', type: 'personal', status: 'active', role: 'owner' }); fixture.detectChanges();
    const panel = fixture.componentInstance; panel.archive(); expect(mutate).not.toHaveBeenCalled();
    panel.archiving.set(true); panel.archive(); expect(mutate).toHaveBeenCalledExactlyOnceWith('POST', `/api/v1/vaults/${id}/archive`, null); fixture.destroy();
  });
});
