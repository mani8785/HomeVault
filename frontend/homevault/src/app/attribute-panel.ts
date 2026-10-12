import { Component, Input, OnChanges, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Panel } from './panel';

interface OrdinaryAttribute { name: string; value: string }

@Component({
  selector: 'hv-attributes', imports: [FormsModule],
  template: `
    <h3>Ordinary attributes</h3><p class="hint">Visible to Vault members. Do not store passwords or other secrets here.</p>
    @if (error()) { <p role="alert" class="error-summary">{{ error() }}</p> } @if (notice()) { <p role="status">{{ notice() }}</p> }
    <dl class="metadata-list">@for (row of rows(); track row.name) { <dt>{{ row.name }}</dt><dd><span class="private-text">{{ row.value }}</span>@if (writable) { <div class="row-actions"><button (click)="edit(row)" [disabled]="busy()">Change</button><button (click)="removing.set(row.name)" [disabled]="busy()">Remove</button></div> }</dd> }</dl>
    @if (!rows().length) { <p class="hint">No ordinary attributes.</p> }
    @if (removing(); as name) { <div class="confirmation"><p>Remove {{ name }}?</p><button (click)="remove(name)" [disabled]="busy()">Confirm remove</button><button (click)="removing.set('')">Cancel</button></div> }
    @if (writable) { <form #form="ngForm" (ngSubmit)="save()" class="panel-form"><h4>{{ editing ? 'Change attribute' : 'Add attribute' }}</h4>
      <label for="attribute-name">Name</label><input id="attribute-name" name="name" [(ngModel)]="name" required [readonly]="editing" autocomplete="off">
      <label for="attribute-value">Value</label><textarea id="attribute-value" name="value" [(ngModel)]="value" required rows="3" autocomplete="off"></textarea>
      <button type="submit" [disabled]="busy() || form.invalid">{{ editing ? 'Save change' : 'Add attribute' }}</button>@if (editing) { <button type="button" (click)="cancelEdit()">Cancel</button> }
    </form> }
  `,
})
export class AttributePanel extends Panel implements OnChanges {
  @Input({ required: true }) assetId = ''; @Input() writable = false;
  readonly rows = signal<OrdinaryAttribute[]>([]); readonly removing = signal('');
  name = ''; value = ''; editing = false;
  ngOnChanges(): void { this.reset(); this.rows.set([]); this.removing.set(''); this.cancelEdit(); this.refresh(); }
  private get path(): string { return `/api/v1/assets/${this.assetId}/attributes`; }
  refresh(): void { this.read<{ attributes: OrdinaryAttribute[] }>(this.path, result => this.rows.set(result.attributes)); }
  edit(row: OrdinaryAttribute): void { this.name = row.name; this.value = row.value; this.editing = true; }
  cancelEdit(): void { this.name = ''; this.value = ''; this.editing = false; }
  save(): void {
    if (!this.writable || !this.name.trim() || !this.value.trim()) return;
    const body = this.editing ? { name: this.name, value: this.value } : { name: this.name, value: this.value, sensitivity: 'ordinary' };
    this.write('POST', `${this.path}/${this.editing ? 'change' : 'add'}`, body, () => { this.cancelEdit(); this.refresh(); });
  }
  remove(name: string): void { if (this.writable) this.write('POST', `${this.path}/remove`, { name }, () => { this.removing.set(''); this.refresh(); }); }
}
