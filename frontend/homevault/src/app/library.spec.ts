import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { Api, Asset, LibraryVault, Page } from './api';
import { LibraryPage } from './library';

describe('Library selection lifetime', () => {
  it('drops a late detail response after switching Vaults', () => {
    const detail = new Subject<Asset>();
    const first: LibraryVault = { id: '11111111-1111-1111-1111-111111111111', name: 'First', type: 'personal', status: 'active', role: 'owner' };
    const second = { ...first, id: '22222222-2222-2222-2222-222222222222', name: 'Second' };
    const read = vi.fn(() => of<Page<LibraryVault>>({ items: [], hasMore: false }));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Api, useValue: { read, asset: () => detail } }] });
    const fixture = TestBed.createComponent(LibraryPage); const page = fixture.componentInstance;
    page.selectVault(first); page.selectAsset({ id: first.id, vaultId: first.id, name: 'Old' }); page.selectVault(second);
    detail.next({ id: first.id, vaultId: first.id, name: 'Stale' });
    expect(page.selectedAsset()).toBeNull(); expect(page.selectedVault()?.id).toBe(second.id);
    fixture.destroy();
  });
});
