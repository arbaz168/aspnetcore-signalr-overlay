import type { EventEnvelope } from './types'

export type Alert =
  | { kind: 'single'; event: EventEnvelope }
  | { kind: 'summary'; count: number; totalMinor: number; currency: string }

/**
 * Plays alerts one at a time, in order. A raid of 500 follows must not keep the screen busy for
 * forty minutes, so once the queue is full, further events fold into one summary alert at the end.
 */
export class AlertQueue {
  private readonly items: Alert[] = []
  private readonly maxQueued: number

  constructor(maxQueued = 10) {
    this.maxQueued = Math.max(1, maxQueued)
  }

  get size(): number {
    return this.items.length
  }

  enqueue(event: EventEnvelope): void {
    if (event.type === 'GoalSet') {
      return
    }

    const tail = this.items.at(-1)

    if (tail?.kind === 'summary') {
      tail.count += 1
      tail.totalMinor += event.amountMinor ?? 0
      return
    }

    if (this.items.length < this.maxQueued) {
      this.items.push({ kind: 'single', event })
      return
    }

    // Full: the last single alert and this event become a summary that absorbs everything after.
    const last = this.items.pop() as Extract<Alert, { kind: 'single' }>
    this.items.push({
      kind: 'summary',
      count: 2,
      totalMinor: (last.event.amountMinor ?? 0) + (event.amountMinor ?? 0),
      currency: event.currency,
    })
  }

  next(): Alert | undefined {
    return this.items.shift()
  }
}
