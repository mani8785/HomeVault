import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LibraryVault, Page, Role, validId } from './api';
import { Panel } from './panel';

interface Member { actorId: string; role: Role }
@Component({
  selector: 'hv-vault-controls', imports: [FormsModule],
  template: `
    <h3>{{ vault.name }}</h3><p class="hint">{{ vault.type }} · {{ vault.status }} · your role: {{ vault.role }}</p>
    @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> } @if (notice()) { <p role="status">{{ notice() }}</p> }
    @if (canManage()) {
      <h4>Members</h4><ul class="record-list">@for (member of members().items; track member.actorId) { <li><span class="record-name">{{ member.actorId }}</span><small>{{ member.role }}</small>
        @if (canChange(member) && vault.status === 'active') { <div class="row-actions"><button (click)="edit(member)" [disabled]="busy()">Change role</button><button (click)="removing.set(member.actorId)" [disabled]="busy()">Remove</button></div> }
        @if (removing() === member.actorId) { <p>Remove this member's access?</p><button (click)="remove(member.actorId)" [disabled]="busy()">Confirm remove</button><button (click)="removing.set('')">Cancel</button> }
      </li> }</ul><div class="paging"><button (click)="refresh(offset - 50)" [disabled]="offset === 0">Previous</button><button (click)="refresh(offset + 50)" [disabled]="!members().hasMore">Next</button></div>
      @if (vault.status === 'active') { <form #form="ngForm" (ngSubmit)="save()" class="panel-form"><h4>{{ editing ? 'Change member role' : 'Add member' }}</h4>
        <label for="member-id">Member ID</label><input id="member-id" name="actor" [(ngModel)]="actor" required [readonly]="editing" autocomplete="off"><p class="hint">Ask the person for the member ID shown on their Account page. The account must already be enabled.</p>
        <label for="member-role">Role</label><select id="member-role" name="role" [(ngModel)]="role">@if (vault.role === 'owner') { <option value="owner">Owner</option><option value="administrator">Administrator</option> }<option value="editor">Editor</option><option value="viewer">Viewer</option></select>
        <button type="submit" [disabled]="busy() || form.invalid">{{ editing ? 'Save role' : 'Add member' }}</button>@if (editing) { <button type="button" (click)="cancelEdit()">Cancel edit</button> }
      </form> }
    }
    @if (vault.role === 'owner' && vault.status === 'active') { <section class="danger-zone"><h4>Archive Vault</h4><p>Archiving makes this Vault read-only, including its memberships. There is no unarchive operation.</p>
      @if (archiving()) { <button (click)="archive()" [disabled]="busy()">Confirm archive {{ vault.name }}</button><button (click)="archiving.set(false)">Cancel</button> } @else { <button (click)="archiving.set(true)">Archive Vault…</button> }
    </section> }
    @if (vault.status === 'archived') { <p>This Vault remains readable. Changes are blocked by the server.</p> }
  `,
})
export class VaultPanel extends Panel implements OnChanges {
  @Input({ required: true }) vault!: LibraryVault; @Output() readonly changed = new EventEmitter<void>();
  readonly members = signal<Page<Member>>({ items: [], hasMore: false }); readonly removing = signal(''); readonly archiving = signal(false);
  offset = 0; actor = ''; role: Role = 'viewer'; editing = false;
  ngOnChanges(): void { this.reset(); this.members.set({ items: [], hasMore: false }); this.removing.set(''); this.archiving.set(false); this.cancelEdit(); if (this.canManage()) this.refresh(0); }
  canManage(): boolean { return this.vault.role === 'owner' || this.vault.role === 'administrator'; }
  canChange(member: Member): boolean { return this.vault.role === 'owner' || this.vault.role === 'administrator' && ['editor', 'viewer'].includes(member.role); }
  private get path(): string { return `/api/v1/vaults/${this.vault.id}`; }
  refresh(offset: number): void { this.offset = offset; this.read<Page<Member>>(`${this.path}/members?offset=${offset}`, result => this.members.set(result)); }
  edit(member: Member): void { this.actor = member.actorId; this.role = member.role; this.editing = true; }
  cancelEdit(): void { this.actor = ''; this.role = 'viewer'; this.editing = false; }
  save(): void {
    if (!this.canManage() || this.vault.status !== 'active') return;
    if (!validId(this.actor)) { this.error.set('Enter a valid member ID from the person’s Account page.'); return; }
    this.write(this.editing ? 'PUT' : 'POST', this.editing ? `${this.path}/members/${this.actor}/role` : `${this.path}/members`, this.editing ? { role: this.role } : { targetActorId: this.actor, role: this.role }, () => this.changed.emit());
  }
  remove(actor: string): void { if (this.canManage() && this.vault.status === 'active') this.write('DELETE', `${this.path}/members/${actor}`, null, () => this.changed.emit()); }
  archive(): void { if (this.archiving() && this.vault.role === 'owner' && this.vault.status === 'active') this.write('POST', `${this.path}/archive`, null, () => this.changed.emit()); }
}
