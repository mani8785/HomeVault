import { Component, EventEmitter, Input, OnChanges, Output, signal } from '@angular/core';
import { Asset } from './api';
import { AttributePanel } from './attribute-panel';
import { EvidencePanel } from './evidence-panel';
import { RelationshipPanel, ReminderPanel } from './related-panels';
import { SensitivePanel } from './sensitive-panel';

@Component({
  selector: 'hv-inspector', imports: [AttributePanel, EvidencePanel, RelationshipPanel, ReminderPanel, SensitivePanel],
  template: `
    <nav class="inspector-tabs" aria-label="Record sections">@for (section of sections; track section) { <button (click)="tab.set(section)" [attr.aria-pressed]="tab() === section">{{ section }}</button> }</nav>
    @switch (tab()) {
      @case ('Info') { <dl class="metadata-list"><dt>Name</dt><dd>{{ asset.name }}</dd><dt>Asset ID</dt><dd class="record-name">{{ asset.id }}</dd><dt>Vault ID</dt><dd class="record-name">{{ asset.vaultId }}</dd></dl><p class="hint">Choose a section to inspect its current data. Private content is never included in library searches.</p> }
      @case ('Attributes') { <hv-attributes [assetId]="asset.id" [writable]="writable" (unavailable)="unavailable.emit()" /> }
      @case ('Evidence') { <hv-evidence [assetId]="asset.id" [writable]="writable" (unavailable)="unavailable.emit()" /> }
      @case ('Relationships') { <hv-relationships [assetId]="asset.id" [vaultId]="asset.vaultId" [writable]="writable" (unavailable)="unavailable.emit()" /> }
      @case ('Reminders') { <hv-reminders [assetId]="asset.id" [vaultId]="asset.vaultId" [writable]="writable" (unavailable)="unavailable.emit()" /> }
      @case ('Sensitive') { <hv-sensitive [assetId]="asset.id" [writable]="writable && mayReveal" [mayReveal]="mayReveal" (unavailable)="unavailable.emit()" /> }
    }
  `,
})
export class Inspector implements OnChanges {
  @Input({ required: true }) asset!: Asset; @Input() writable = true; @Input() mayReveal = true;
  @Output() readonly unavailable = new EventEmitter<void>();
  readonly sections = ['Info', 'Attributes', 'Evidence', 'Relationships', 'Reminders', 'Sensitive']; readonly tab = signal('Info');
  ngOnChanges(): void { this.tab.set('Info'); }
}
