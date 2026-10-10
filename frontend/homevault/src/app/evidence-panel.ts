import { Component, Input, OnChanges, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Panel } from './panel';

interface Evidence { id: string; label: string; kind: 'url' | 'note' }
@Component({
  selector: 'hv-evidence', imports: [FormsModule],
  template: `
    <h3>Evidence</h3><p class="hint">URL references and notes. File uploads and document previews are not available yet.</p>
    @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> } @if (notice()) { <p role="status">{{ notice() }}</p> }
    <ul class="record-list">@for (row of rows(); track row.id) { <li><strong>{{ row.label }}</strong><small>{{ row.kind }}</small><div class="row-actions"><button (click)="reveal(row)" [disabled]="busy()">Read content</button>@if (writable) { <button (click)="removing.set(row.id)">Remove</button> }</div>
      @if (shownId() === row.id) { <pre class="private-text">{{ shown() }}</pre>@if (row.kind === 'url') { <p><a [href]="shown()" target="_blank" rel="noopener noreferrer" referrerpolicy="no-referrer">Open URL in a new tab</a></p> }<button (click)="hide()">Hide content</button> }
      @if (removing() === row.id) { <p>Remove this Evidence?</p><button (click)="remove(row.id)" [disabled]="busy()">Confirm remove</button><button (click)="removing.set('')">Cancel</button> }
    </li> }</ul>
    @if (!rows().length) { <p class="hint">No Evidence yet.</p> }
    @if (writable) { <form #form="ngForm" (ngSubmit)="add()" class="panel-form"><h4>Add Evidence</h4>
      <label for="evidence-label">Label</label><input id="evidence-label" name="label" [(ngModel)]="label" required autocomplete="off">
      <label for="evidence-kind">Kind</label><select id="evidence-kind" name="kind" [(ngModel)]="kind"><option value="note">Note</option><option value="url">URL reference</option></select>
      <label for="evidence-content">Content</label><textarea id="evidence-content" name="content" [(ngModel)]="content" required rows="4" autocomplete="off"></textarea><p class="hint">Ordinary plaintext. URLs are stored as references and never fetched automatically.</p>
      <button [disabled]="busy() || form.invalid" type="submit">Add Evidence</button>
    </form> }
  `,
})
export class EvidencePanel extends Panel implements OnChanges {
  @Input({ required: true }) assetId = ''; @Input() writable = false;
  readonly rows = signal<Evidence[]>([]); readonly shown = signal(''); readonly shownId = signal(''); readonly removing = signal('');
  label = ''; kind: 'url' | 'note' = 'note'; content = ''; private revealVersion = 0;
  ngOnChanges(): void { this.reset(); this.rows.set([]); this.removing.set(''); this.label = ''; this.refresh(); }
  private get path(): string { return `/api/v1/assets/${this.assetId}/evidence`; }
  refresh(): void { this.read<{ evidence: Evidence[] }>(this.path, result => this.rows.set(result.evidence)); }
  hide(): void { this.revealVersion++; this.shown.set(''); this.shownId.set(''); }
  protected override clearPrivate(): void { this.hide(); this.content = ''; }
  reveal(row: Evidence): void {
    this.hide(); const version = this.revealVersion;
    this.read<{ content: string }>(`${this.path}/${row.id}/content`, value => { if (version === this.revealVersion) { this.shown.set(value.content); this.shownId.set(row.id); } }, 'content');
  }
  add(): void {
    if (!this.writable || !this.label.trim() || !this.content.trim()) return;
    this.write('POST', this.path, { id: crypto.randomUUID(), label: this.label, kind: this.kind, content: this.content }, () => { this.label = ''; this.refresh(); });
  }
  remove(id: string): void { if (this.writable) this.write('DELETE', `${this.path}/${id}`, null, () => { this.removing.set(''); this.refresh(); }); }
}
