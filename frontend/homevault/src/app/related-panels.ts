import { Component, Input, OnChanges, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe, SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Asset, Page } from './api';
import { Panel } from './panel';

interface Relationship { id: string; sourceAssetId: string; targetAssetId: string; status: string }
interface Reminder { id: string; assetId: string; dueAt: string; status: 'pending' | 'completed' | 'cancelled' }

@Component({
  selector: 'hv-relationships', imports: [FormsModule, SlicePipe, RouterLink],
  template: `
    <h3>Relationships</h3><p class="hint">A directed “covers” link, such as insurance covering a vehicle.</p>
    @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> } @if (notice()) { <p role="status">{{ notice() }}</p> }
    <ul class="record-list">@for (row of rows().items; track row.id) { <li><span>{{ row.sourceAssetId === assetId ? 'This Asset covers' : 'This Asset is covered by' }}</span><p><a [routerLink]="['/assets', row.sourceAssetId === assetId ? row.targetAssetId : row.sourceAssetId]">Open linked Asset →</a></p><small>{{ row.status }}</small>
      @if (writable && row.status === 'active') { <button (click)="removing.set(row.id)">Remove link</button> }
      @if (removing() === row.id) { <p>Remove this Relationship? Both Assets remain.</p><button (click)="remove(row.id)" [disabled]="busy()">Confirm remove</button><button (click)="removing.set('')">Cancel</button> }
    </li> }</ul><div class="paging"><button (click)="refresh(offset - 50)" [disabled]="offset === 0">Previous</button><button (click)="refresh(offset + 50)" [disabled]="!rows().hasMore">Next</button></div>
    @if (writable) { <form #form="ngForm" (ngSubmit)="add()" class="panel-form"><h4>Create a Covers link</h4>
      <label for="relationship-search">Find the other Asset</label><div class="row-actions"><input id="relationship-search" name="search" [(ngModel)]="search" maxlength="200" type="search"><button type="button" (click)="findAssets(0)">Find</button></div>
      <label for="relationship-target">Other Asset in this Vault</label><select id="relationship-target" name="target" [(ngModel)]="target" required><option value="">Choose an Asset</option>@for (asset of candidates().items; track asset.id) { @if (asset.id !== assetId) { <option [value]="asset.id">{{ asset.name }} · {{ asset.id | slice:0:8 }}</option> } }</select>
      <div class="paging"><button type="button" (click)="findAssets(candidateOffset - 50)" [disabled]="candidateOffset === 0">Previous Assets</button><button type="button" (click)="findAssets(candidateOffset + 50)" [disabled]="!candidates().hasMore">More Assets</button></div>
      <label for="relationship-direction">Direction</label><select id="relationship-direction" name="direction" [(ngModel)]="outgoing"><option [ngValue]="true">This Asset covers the other Asset</option><option [ngValue]="false">The other Asset covers this Asset</option></select>
      <button type="submit" [disabled]="busy() || form.invalid">Create Relationship</button>
    </form> }
  `,
})
export class RelationshipPanel extends Panel implements OnChanges {
  @Input({ required: true }) assetId = ''; @Input({ required: true }) vaultId = ''; @Input() writable = false;
  readonly rows = signal<Page<Relationship>>({ items: [], hasMore: false }); readonly candidates = signal<Page<Asset>>({ items: [], hasMore: false }); readonly removing = signal('');
  offset = 0; candidateOffset = 0; search = ''; target = ''; outgoing = true;
  ngOnChanges(): void { this.reset(); this.rows.set({ items: [], hasMore: false }); this.candidates.set({ items: [], hasMore: false }); this.target = ''; this.removing.set(''); this.refresh(0); if (this.writable) this.findAssets(0); }
  private get path(): string { return `/api/v1/vaults/${this.vaultId}/relationships`; }
  refresh(offset: number): void { this.offset = offset; this.read<Page<Relationship>>(`${this.path}?assetId=${this.assetId}&offset=${offset}`, result => this.rows.set(result)); }
  findAssets(offset: number): void { this.candidateOffset = offset; this.target = ''; this.read<Page<Asset>>(`/api/v1/vaults/${this.vaultId}/assets?offset=${offset}&search=${encodeURIComponent(this.search)}`, result => this.candidates.set(result), 'candidates'); }
  add(): void {
    if (!this.writable || !this.target || this.target === this.assetId) return;
    this.write('POST', this.path, { id: crypto.randomUUID(), sourceAssetId: this.outgoing ? this.assetId : this.target, targetAssetId: this.outgoing ? this.target : this.assetId, kind: 'covers' }, () => { this.target = ''; this.refresh(0); });
  }
  remove(id: string): void { if (this.writable) this.write('DELETE', `${this.path}/${id}`, null, () => { this.removing.set(''); this.refresh(this.offset); }); }
}

@Component({
  selector: 'hv-reminders', imports: [FormsModule, DatePipe],
  template: `
    <h3>Reminders</h3><p class="hint">Recorded deadlines. No automatic notification or recurring schedule is sent.</p>
    @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> } @if (notice()) { <p role="status">{{ notice() }}</p> }
    <ul class="record-list">@for (row of rows().items; track row.id) { <li><strong>{{ row.dueAt | date:'medium' }}</strong><small>{{ row.status }}</small><div class="row-actions"><button (click)="reveal(row)">Read action</button>
      @if (writable && row.status === 'pending') { <button (click)="edit(row)">Update</button><button (click)="finishing.set({id: row.id, operation: 'complete'})">Complete</button><button (click)="finishing.set({id: row.id, operation: 'cancel'})">Cancel Reminder</button> }</div>
      @if (shownId() === row.id) { <pre class="private-text">{{ shown() }}</pre><button (click)="hide()">Hide action</button> }
      @if (finishing()?.id === row.id) { <p>Confirm {{ finishing()!.operation }}? This is a terminal status.</p><button (click)="finish()" [disabled]="busy()">Confirm</button><button (click)="finishing.set(null)">Keep pending</button> }
    </li> }</ul><div class="paging"><button (click)="refresh(offset - 50)" [disabled]="offset === 0">Previous</button><button (click)="refresh(offset + 50)" [disabled]="!rows().hasMore">Next</button></div>
    @if (writable) { <form #form="ngForm" (ngSubmit)="save()" class="panel-form"><h4>{{ editing ? 'Update Reminder' : 'Create Reminder' }}</h4>
      <label for="reminder-due">Due date and time (your local timezone)</label><input id="reminder-due" name="due" [(ngModel)]="due" type="datetime-local" required step="1">
      <label for="reminder-action">Action</label><textarea id="reminder-action" name="action" [(ngModel)]="action" rows="3" required autocomplete="off"></textarea><p class="hint">Action text is ordinary plaintext. Updating replaces both the date and action.</p>
      <button type="submit" [disabled]="busy() || form.invalid">{{ editing ? 'Save update' : 'Create Reminder' }}</button>@if (editing) { <button type="button" (click)="cancelEdit()">Cancel edit</button> }
    </form> }
  `,
})
export class ReminderPanel extends Panel implements OnChanges {
  @Input({ required: true }) assetId = ''; @Input({ required: true }) vaultId = ''; @Input() writable = false;
  readonly rows = signal<Page<Reminder>>({ items: [], hasMore: false }); readonly shown = signal(''); readonly shownId = signal('');
  readonly finishing = signal<{ id: string; operation: 'complete' | 'cancel' } | null>(null);
  offset = 0; due = ''; action = ''; editing = ''; private version = 0;
  private originalDue = ''; private originalLocalDue = '';
  ngOnChanges(): void { this.reset(); this.rows.set({ items: [], hasMore: false }); this.finishing.set(null); this.cancelEdit(); this.refresh(0); }
  private get path(): string { return `/api/v1/vaults/${this.vaultId}/reminders`; }
  refresh(offset: number): void { this.offset = offset; this.hide(); this.read<Page<Reminder>>(`${this.path}?assetId=${this.assetId}&offset=${offset}`, result => this.rows.set(result)); }
  hide(): void { this.version++; this.shown.set(''); this.shownId.set(''); }
  protected override clearPrivate(): void { this.hide(); this.action = ''; }
  reveal(row: Reminder): void { this.hide(); const version = this.version; this.read<{ action: string }>(`${this.path}/${row.id}/action`, result => { if (version === this.version) { this.shownId.set(row.id); this.shown.set(result.action); } }, 'action'); }
  edit(row: Reminder): void {
    this.clearPrivate(); this.editing = row.id;
    const date = new Date(row.dueAt); this.due = new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 19);
    this.originalDue = row.dueAt; this.originalLocalDue = this.due;
    // Editing deliberately starts with a blank replacement action; no automatic private read.
  }
  cancelEdit(): void { this.clearPrivate(); this.editing = ''; this.due = ''; }
  save(): void {
    if (!this.writable || !this.action.trim() || !this.due) return;
    const date = new Date(this.due); if (!Number.isFinite(date.getTime())) { this.error.set('Choose a valid due date and time.'); return; }
    const dueAt = this.editing && this.due === this.originalLocalDue ? this.originalDue : date.toISOString();
    const body = this.editing ? { action: this.action, dueAt } : { id: crypto.randomUUID(), assetId: this.assetId, action: this.action, dueAt };
    this.write(this.editing ? 'PUT' : 'POST', this.editing ? `${this.path}/${this.editing}` : this.path, body, () => { this.cancelEdit(); this.refresh(0); });
  }
  finish(): void { const target = this.finishing(); if (this.writable && target) this.write('POST', `${this.path}/${target.id}/${target.operation}`, null, () => { this.finishing.set(null); this.refresh(this.offset); }); }
}
