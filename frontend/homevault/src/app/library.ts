import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { Api, Asset, failureMessage, LibraryVault, Page } from './api';

@Component({
  imports: [FormsModule, RouterLink],
  template: `
    <div class="library-heading"><div><p class="eyebrow">HOMEVAULT</p><h1>My library</h1></div><button (click)="loadVaults(0)">Refresh library</button></div>
    @if (error()) { <p class="error-summary" role="alert">{{ error() }}</p> }
    <nav class="mobile-panes" aria-label="Library panes"><button (click)="pane.set('vaults')" [attr.aria-pressed]="pane() === 'vaults'">Vaults</button><button (click)="pane.set('assets')" [disabled]="!selectedVault()" [attr.aria-pressed]="pane() === 'assets'">Assets</button><button (click)="pane.set('details')" [disabled]="!selectedAsset()" [attr.aria-pressed]="pane() === 'details'">Details</button></nav>
    <div class="library-layout" [attr.data-pane]="pane()">
      <aside class="library-sidebar" aria-label="Vaults">
        <div class="pane-heading"><h2>Vaults</h2><a routerLink="/vaults/new" aria-label="Create a Vault">＋ New</a></div>
        <form (ngSubmit)="loadVaults(0)" class="compact-search"><label for="vault-search">Find a Vault</label><div><input id="vault-search" name="vaultSearch" [(ngModel)]="vaultSearch" maxlength="200" type="search"><button type="submit">Find</button></div></form>
        @if (vaultLoading()) { <p role="status">Loading Vaults…</p> }
        <ul class="vault-tree">@for (vault of vaults().items; track vault.id) {
          <li><button type="button" [class.selected]="selectedVault()?.id === vault.id" [attr.aria-pressed]="selectedVault()?.id === vault.id" (click)="selectVault(vault)"><span aria-hidden="true">▱</span><span>{{ vault.name }}<small>{{ vault.type }} · {{ vault.role }}{{ vault.status === 'archived' ? ' · archived' : '' }}</small></span></button></li>
        }</ul>
        @if (!vaultLoading() && !vaults().items.length) { <p class="empty-state">No Vaults found. Create one or ask an Owner to add you.</p> }
        <div class="paging"><button (click)="loadVaults(vaultOffset - 50)" [disabled]="vaultLoading() || vaultOffset === 0">Previous</button><button (click)="loadVaults(vaultOffset + 50)" [disabled]="vaultLoading() || !vaults().hasMore">Next</button></div>
      </aside>
      <section class="library-list" aria-label="Assets">
        <div class="pane-heading"><h2>{{ selectedVault()?.name || 'Assets' }}</h2>@if (canWrite()) { <a [routerLink]="['/vaults', selectedVault()!.id, 'assets', 'new']">＋ Add Asset</a> }</div>
        @if (selectedVault(); as vault) {
          <form (ngSubmit)="loadAssets(0)" class="compact-search"><label for="asset-search">Find an Asset by name</label><div><input id="asset-search" name="assetSearch" [(ngModel)]="assetSearch" maxlength="200" type="search"><button type="submit">Find</button></div></form>
          @if (assetLoading()) { <p role="status">Loading Assets…</p> }
          <table class="asset-table"><thead><tr><th scope="col">Name</th><th scope="col">Record</th></tr></thead><tbody>
          @for (asset of assets().items; track asset.id) { <tr [class.selected]="selectedAsset()?.id === asset.id"><td><button (click)="selectAsset(asset)" [attr.aria-pressed]="selectedAsset()?.id === asset.id">{{ asset.name }}</button></td><td>Asset</td></tr> }
          </tbody></table>
          @if (!assetLoading() && !assets().items.length) { <p class="empty-state">No Assets found in this Vault.</p> }
          <div class="paging"><button (click)="loadAssets(assetOffset - 50)" [disabled]="assetLoading() || assetOffset === 0">Previous</button><span>Page {{ assetOffset / 50 + 1 }}</span><button (click)="loadAssets(assetOffset + 50)" [disabled]="assetLoading() || !assets().hasMore">Next</button></div>
          <p class="hint">{{ vault.status === 'archived' ? 'Archived Vault · read only' : 'Your role: ' + vault.role }}</p>
        } @else { <div class="empty-state"><h3>Choose a Vault</h3><p>Your records appear here. Each Vault controls ownership and access.</p></div> }
      </section>
      <section class="library-inspector" aria-label="Selected record">
        <div class="pane-heading"><h2>Inspector</h2></div>
        @if (selectedAsset(); as asset) {
          <p class="eyebrow">ASSET</p><h2 class="record-name">{{ asset.name }}</h2><p class="hint">{{ selectedVault()?.name }}</p>
          <a [routerLink]="['/assets', asset.id]">Open record & details →</a>
        } @else { <div class="empty-state"><h3>Your record, in detail</h3><p>Select an Asset to inspect its information.</p></div> }
      </section>
    </div>
  `,
})
export class LibraryPage {
  private readonly api = inject(Api);
  private vaultRead?: Subscription; private assetRead?: Subscription; private detailRead?: Subscription;
  readonly vaults = signal<Page<LibraryVault>>({ items: [], hasMore: false });
  readonly assets = signal<Page<Asset>>({ items: [], hasMore: false });
  readonly selectedVault = signal<LibraryVault | null>(null); readonly selectedAsset = signal<Asset | null>(null);
  readonly error = signal(''); readonly vaultLoading = signal(false); readonly assetLoading = signal(false);
  readonly pane = signal<'vaults' | 'assets' | 'details'>('vaults');
  vaultSearch = ''; assetSearch = ''; vaultOffset = 0; assetOffset = 0;
  constructor() {
    inject(DestroyRef).onDestroy(() => { this.vaultRead?.unsubscribe(); this.assetRead?.unsubscribe(); this.detailRead?.unsubscribe(); });
    this.loadVaults(0);
  }
  canWrite(): boolean { const vault = this.selectedVault(); return !!vault && vault.status === 'active' && vault.role !== 'viewer'; }
  loadVaults(offset: number): void {
    this.vaultRead?.unsubscribe(); this.clearSelection(); this.vaultLoading.set(true); this.error.set(''); this.vaultOffset = offset;
    this.vaults.set({ items: [], hasMore: false });
    this.vaultRead = this.api.read<Page<LibraryVault>>(`/api/v1/vaults?offset=${offset}&search=${encodeURIComponent(this.vaultSearch)}`).subscribe({
      next: value => { this.vaults.set(value); this.vaultLoading.set(false); },
      error: error => { this.error.set(failureMessage(error)); this.vaultLoading.set(false); },
    });
  }
  selectVault(vault: LibraryVault): void { this.clearSelection(); this.selectedVault.set(vault); this.assetSearch = ''; this.pane.set('assets'); this.loadAssets(0); }
  loadAssets(offset: number): void {
    const vault = this.selectedVault(); if (!vault) return;
    this.assetRead?.unsubscribe(); this.detailRead?.unsubscribe(); this.selectedAsset.set(null); this.assets.set({ items: [], hasMore: false });
    this.assetLoading.set(true); this.assetOffset = offset; this.error.set('');
    this.assetRead = this.api.read<Page<Asset>>(`/api/v1/vaults/${vault.id}/assets?offset=${offset}&search=${encodeURIComponent(this.assetSearch)}`).subscribe({
      next: value => { this.assets.set(value); this.assetLoading.set(false); },
      error: error => { this.clearSelection(); this.error.set(failureMessage(error)); },
    });
  }
  selectAsset(asset: Asset): void {
    this.detailRead?.unsubscribe(); this.selectedAsset.set(null); this.error.set('');
    this.detailRead = this.api.asset(asset.id).subscribe({ next: value => { this.selectedAsset.set(value); this.pane.set('details'); }, error: error => { this.clearSelection(); this.error.set(failureMessage(error)); } });
  }
  private clearSelection(): void {
    this.assetRead?.unsubscribe(); this.detailRead?.unsubscribe(); this.selectedAsset.set(null); this.selectedVault.set(null);
    this.assets.set({ items: [], hasMore: false }); this.assetLoading.set(false);
    this.pane.set('vaults');
  }
}
