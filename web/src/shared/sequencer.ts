import type { EventEnvelope, ResumeResult } from './types'

export type ReceiveOutcome = 'applied' | 'duplicate' | 'gap' | 'buffered'

/**
 * Applies a channel's events exactly once and in order, whatever order the network delivers them in.
 *
 * Delivery is at least once and concurrent publishers can broadcast out of order, so the client checks
 * each sequence number:
 * - already seen: dropped as a duplicate
 * - the next one: applied
 * - further ahead: something was missed, so the caller fetches it with Resume and events are buffered meanwhile
 */
export class EventSequencer {
  private last: number
  private resyncing = false
  private buffer: EventEnvelope[] = []
  private readonly onEvent: (event: EventEnvelope) => void

  constructor(onEvent: (event: EventEnvelope) => void, lastSequence = 0) {
    this.onEvent = onEvent
    this.last = lastSequence
  }

  get lastSequence(): number {
    return this.last
  }

  receive(event: EventEnvelope): ReceiveOutcome {
    if (this.resyncing) {
      this.buffer.push(event)
      return 'buffered'
    }

    if (event.sequence <= this.last) {
      return 'duplicate'
    }

    if (event.sequence === this.last + 1) {
      this.last = event.sequence
      this.onEvent(event)
      return 'applied'
    }

    this.resyncing = true
    this.buffer.push(event)
    return 'gap'
  }

  /** Call before asking the server for missed events, on a gap or a reconnect. */
  beginResync(): void {
    this.resyncing = true
  }

  /**
   * Applies what the server returned, then anything that arrived live while waiting.
   * Returns 'gap' when the buffered events still leave a hole, and the caller should resume again.
   *
   * With replay off (a brand new overlay), missed events only move the position:
   * a fresh browser source should not play old alerts.
   */
  completeResync(result: ResumeResult, replay = true): 'gap' | undefined {
    if (replay) {
      for (const event of result.events) {
        if (event.sequence > this.last) {
          this.onEvent(event)
        }
      }
    }

    // The server's position is authoritative, including when it is behind ours (a reset database).
    this.last = result.latestSequence
    this.resyncing = false

    const pending = this.buffer.sort((a, b) => a.sequence - b.sequence)
    this.buffer = []

    let outcome: 'gap' | undefined
    for (const event of pending) {
      if (this.receive(event) === 'gap') {
        outcome = 'gap'
      }
    }

    return outcome
  }
}
