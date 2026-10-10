import { DestroyRef, Directive, EventEmitter, inject, Output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { Api, failureMessage } from './api';

/** Owns request lifetimes for a selected-record panel; never retries mutations. */
@Directive()
export abstract class Panel {
  protected readonly api = inject(Api);
  protected readonly destroy = inject(DestroyRef);
  private reads = new Map<string, Subscription>(); private mutation?: Subscription;
  @Output() readonly unavailable = new EventEmitter<void>();
  readonly busy = signal(false); readonly error = signal(''); readonly notice = signal('');
  constructor() { this.destroy.onDestroy(() => this.reset()); }
  protected clearPrivate(): void {}
  protected reset(): void {
    this.reads.forEach(read => read.unsubscribe()); this.reads.clear(); this.mutation?.unsubscribe();
    this.clearPrivate(); this.busy.set(false); this.error.set(''); this.notice.set('');
  }
  protected read<T>(path: string, receive: (value: T) => void, key = 'list'): void {
    this.reads.get(key)?.unsubscribe();
    this.reads.set(key, this.api.read<T>(path).subscribe({ next: receive, error: error => this.failed(error) }));
  }
  protected write<T>(method: 'POST' | 'PUT' | 'DELETE', path: string, body: unknown, receive: (value: T) => void, clear = true): void {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(''); this.notice.set('');
    this.mutation = this.api.mutate<T>(method, path, body).subscribe({
      next: value => { this.busy.set(false); if (clear) this.clearPrivate(); this.notice.set(clear ? 'Saved.' : ''); receive(value); },
      error: error => { this.busy.set(false); this.failed(error); },
    });
  }
  protected failed(error: unknown): void {
    this.clearPrivate(); this.error.set(failureMessage(error));
    if (error instanceof HttpErrorResponse && [401, 403, 404].includes(error.status)) {
      this.reads.forEach(read => read.unsubscribe()); this.reads.clear(); this.unavailable.emit();
    }
  }
}
