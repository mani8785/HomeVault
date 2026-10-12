import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Panel } from './panel';

@Component({
  selector: 'hv-rename', imports: [FormsModule],
  template: `
    @if (writable) {
      <form (ngSubmit)="save()" class="panel-form" [attr.aria-busy]="busy()">
        <h4>Rename {{ kind === 'vaults' ? 'Vault' : 'Asset' }}</h4>
        <label [for]="kind + '-rename'">New name</label>
        <input [id]="kind + '-rename'" name="newName" [(ngModel)]="draft" required autocomplete="off" [disabled]="busy()">
        <p class="hint">Names are visible to Vault members. Keep secrets in Sensitive attributes.</p>
        @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> }
        @if (conflict()) { <p>Your draft is kept here. Note it before refreshing the library, then reopen this record and review its current name before retrying.</p> }
        <button type="submit" [disabled]="busy() || conflict() || !draft.trim()">{{ busy() ? 'Saving…' : 'Save name' }}</button>
      </form>
    }
  `,
})
export class RenamePanel extends Panel implements OnChanges {
  @Input({ required: true }) id!: string;
  @Input({ required: true }) name!: string;
  @Input({ required: true }) kind!: 'vaults' | 'assets';
  @Input() writable = false;
  @Output() readonly changed = new EventEmitter<void>();
  readonly conflict = signal(false);
  draft = '';
  ngOnChanges(): void { this.reset(); this.draft = this.name; this.conflict.set(false); }
  save(): void {
    if (!this.writable || this.conflict() || !this.draft.trim()) return;
    this.write('PUT', `/api/v1/${this.kind}/${encodeURIComponent(this.id)}/name`, { name: this.draft, expectedName: this.name }, () => this.changed.emit());
  }
  protected override failed(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 409 && error.error?.code === 'name_conflict') this.conflict.set(true);
    super.failed(error);
    if (error instanceof HttpErrorResponse && error.status === 409 && error.error?.code === 'vault_archived') this.unavailable.emit();
  }
}
