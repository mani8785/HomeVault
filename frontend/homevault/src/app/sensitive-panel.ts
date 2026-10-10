import { Component, Input, OnChanges, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Panel } from './panel';

interface SensitiveMetadata { id: string; name: string }
@Component({
  selector: 'hv-sensitive', imports: [FormsModule],
  template: `
    <h3>Sensitive attributes</h3><p class="hint">Values stay hidden until deliberately revealed. Owner/Administrator access and an unlocked encrypted host are required.</p>
    @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> } @if (notice()) { <p role="status">{{ notice() }}</p> }
    <ul class="record-list">@for (row of rows(); track row.id) { <li><strong>{{ row.name }}</strong>
      @if (mayReveal) { <div class="row-actions"><button (click)="reveal(row.id)" [disabled]="busy()">Reveal value</button>@if (writable) { <button (click)="editing = row.id; name = row.name; hide()" [disabled]="busy()">Replace</button><button (click)="removing.set(row.id)" [disabled]="busy()">Remove</button> }</div> }
      @if (shownId() === row.id) { <pre class="private-text sensitive-value">{{ shown() }}</pre><button (click)="hide()">Hide now</button><p class="hint">Hidden after 30 seconds or when this window loses focus.</p> }
      @if (removing() === row.id) { <p>Remove this encrypted attribute?</p><button (click)="remove(row.id)" [disabled]="busy()">Confirm remove</button><button (click)="removing.set('')">Cancel</button> }
    </li> }</ul>
    @if (writable && available()) { <form #form="ngForm" (ngSubmit)="save()" class="panel-form"><h4>{{ editing ? 'Replace private value' : 'Add Sensitive attribute' }}</h4>
      <label for="sensitive-name">Visible label</label><input id="sensitive-name" name="name" [(ngModel)]="name" required [readonly]="!!editing" autocomplete="off"><p class="hint">The label is ordinary metadata. Keep secrets out of it.</p>
      <label for="sensitive-value">Private value</label><input id="sensitive-value" type="password" name="value" [(ngModel)]="value" required autocomplete="off">
      <button type="submit" [disabled]="busy() || form.invalid">{{ editing ? 'Replace value' : 'Add encrypted value' }}</button>@if (editing) { <button type="button" (click)="cancelEdit()">Cancel</button> }
    </form> }
    <p class="hint">Key initialization, unlock, rotation and recovery are operator terminal tasks.</p>
  `,
})
export class SensitivePanel extends Panel implements OnChanges {
  @Input({ required: true }) assetId = ''; @Input() writable = false; @Input() mayReveal = false;
  readonly rows = signal<SensitiveMetadata[]>([]); readonly available = signal(false);
  readonly shown = signal(''); readonly shownId = signal(''); readonly removing = signal('');
  name = ''; value = ''; editing = ''; private version = 0; private timer?: ReturnType<typeof setTimeout>;
  constructor() {
    super(); const clear = () => this.clearPrivate();
    window.addEventListener('blur', clear); document.addEventListener('visibilitychange', clear);
    this.destroy.onDestroy(() => { window.removeEventListener('blur', clear); document.removeEventListener('visibilitychange', clear); });
  }
  ngOnChanges(): void { this.reset(); this.rows.set([]); this.available.set(false); this.cancelEdit(); this.removing.set(''); this.refresh(); }
  private get path(): string { return `/api/v1/assets/${this.assetId}/sensitive-attributes`; }
  refresh(): void { this.read<{ attributes: SensitiveMetadata[] }>(this.path, result => { this.rows.set(result.attributes); this.available.set(true); }); }
  hide(): void { this.version++; clearTimeout(this.timer); this.shown.set(''); this.shownId.set(''); }
  protected override clearPrivate(): void { this.hide(); this.value = ''; }
  protected override failed(error: unknown): void {
    this.clearPrivate(); this.rows.set([]); this.available.set(false);
    if (error instanceof HttpErrorResponse && [404, 503].includes(error.status)) this.error.set('Sensitive storage or this record is unavailable. Ask your operator to check the encrypted host.');
    else super.failed(error);
  }
  cancelEdit(): void { this.clearPrivate(); this.name = ''; this.editing = ''; }
  reveal(id: string): void {
    if (!this.mayReveal || this.busy()) return;
    this.hide(); const version = this.version;
    this.write<{ value: string }>('POST', `${this.path}/${id}/read`, null, result => {
      if (version !== this.version) return;
      this.shownId.set(id); this.shown.set(result.value); this.timer = setTimeout(() => this.hide(), 30000);
    }, false);
  }
  save(): void {
    if (!this.writable || !this.name.trim() || !this.value.trim()) return;
    const path = this.editing ? `${this.path}/${this.editing}/change` : `${this.path}/add`;
    const body = this.editing ? { value: this.value } : { name: this.name, value: this.value, sensitivity: 'sensitive' };
    this.write('POST', path, body, () => { this.cancelEdit(); this.refresh(); });
  }
  remove(id: string): void { if (this.writable) this.write('POST', `${this.path}/${id}/remove`, null, () => { this.removing.set(''); this.refresh(); }); }
}
