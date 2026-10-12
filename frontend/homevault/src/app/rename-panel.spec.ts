import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, Subject } from 'rxjs';
import { Api } from './api';
import { RenamePanel } from './rename-panel';

const id = '11111111-1111-1111-1111-111111111111';
describe('Record renaming', () => {
  function setup(writable = true) {
    const pending = new Subject<void>(); const mutate = vi.fn(() => pending);
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: { mutate } }] });
    const fixture = TestBed.createComponent(RenamePanel);
    fixture.componentRef.setInput('id', id); fixture.componentRef.setInput('name', ' Original ');
    fixture.componentRef.setInput('kind', 'assets'); fixture.componentRef.setInput('writable', writable); fixture.detectChanges();
    return { fixture, panel: fixture.componentInstance, pending, mutate };
  }
  it('sends exact new and last-read names once and refreshes on success', () => {
    const { fixture, panel, pending, mutate } = setup(); const changed = vi.fn(); panel.changed.subscribe(changed);
    panel.draft = ' Étage/طبقه '; panel.save(); panel.save();
    expect(mutate).toHaveBeenCalledExactlyOnceWith('PUT', `/api/v1/assets/${id}/name`, { name: ' Étage/طبقه ', expectedName: ' Original ' });
    expect(changed).not.toHaveBeenCalled(); pending.next(); expect(changed).toHaveBeenCalledOnce(); fixture.destroy();
  });
  it('keeps the draft after conflict, blocks retry and resets on fresh metadata', () => {
    const { fixture, panel, pending, mutate } = setup(); panel.draft = 'My draft'; panel.save();
    pending.error(new HttpErrorResponse({ status: 409, error: { code: 'name_conflict', detail: 'Private sentinel' } }));
    expect(panel.draft).toBe('My draft'); expect(panel.conflict()).toBe(true); expect(panel.error()).not.toContain('Private sentinel');
    panel.save(); expect(mutate).toHaveBeenCalledOnce();
    fixture.componentRef.setInput('name', 'Current'); fixture.detectChanges();
    expect(panel.draft).toBe('Current'); expect(panel.conflict()).toBe(false); fixture.destroy();
  });
  it('hides renaming for ineligible roles and rejects blank submissions', () => {
    const { fixture, panel, mutate } = setup(false); panel.save(); expect(mutate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    fixture.componentRef.setInput('writable', true); fixture.detectChanges(); panel.draft = ' '; panel.save();
    expect(mutate).not.toHaveBeenCalled(); fixture.destroy();
  });
  it('discards responses from an old selection', () => {
    const { fixture, panel, pending } = setup(); const changed = vi.fn(); panel.changed.subscribe(changed); panel.save();
    fixture.componentRef.setInput('id', '22222222-2222-2222-2222-222222222222'); fixture.detectChanges();
    pending.next(); expect(changed).not.toHaveBeenCalled(); fixture.destroy();
  });
});
