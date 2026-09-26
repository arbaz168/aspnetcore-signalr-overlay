import { describe, expect, it } from 'vitest'
import { EventSequencer } from './sequencer'
import type { EventEnvelope, ResumeResult } from './types'

const goal = { title: null, targetMinor: 0, currentMinor: 0 }

function event(sequence: number): EventEnvelope {
  return {
    id: `evt-${sequence}`,
    sequence,
    type: 'Follow',
    displayName: `viewer ${sequence}`,
    amountMinor: null,
    currency: 'GBP',
    message: null,
    createdAt: new Date(0).toISOString(),
    goal,
  }
}

function resume(latestSequence: number, ...sequences: number[]): ResumeResult {
  return { events: sequences.map(event), latestSequence, skippedCount: 0, currency: 'GBP', goal, connectedOverlays: 0 }
}

function setup(lastSequence = 0) {
  const applied: number[] = []
  const sequencer = new EventSequencer((e) => applied.push(e.sequence), lastSequence)
  return { sequencer, applied }
}

describe('EventSequencer', () => {
  it('applies consecutive events in order', () => {
    const { sequencer, applied } = setup()

    expect([1, 2, 3].map((s) => sequencer.receive(event(s)))).toEqual(['applied', 'applied', 'applied'])
    expect(applied).toEqual([1, 2, 3])
  })

  it('drops an event it has already applied', () => {
    const { sequencer, applied } = setup()
    sequencer.receive(event(1))

    expect(sequencer.receive(event(1))).toBe('duplicate')
    expect(applied).toEqual([1])
  })

  it('reports a gap and holds later events until the missing ones are fetched', () => {
    const { sequencer, applied } = setup()
    sequencer.receive(event(1))

    expect(sequencer.receive(event(3))).toBe('gap')
    expect(sequencer.receive(event(4))).toBe('buffered')
    expect(applied).toEqual([1])

    expect(sequencer.completeResync(resume(3, 2, 3))).toBeUndefined()
    expect(applied).toEqual([1, 2, 3, 4])
    expect(sequencer.lastSequence).toBe(4)
  })

  it('handles two broadcasts arriving in reverse order', () => {
    const { sequencer, applied } = setup()

    expect(sequencer.receive(event(2))).toBe('gap')
    sequencer.completeResync(resume(2, 1, 2))
    expect(sequencer.receive(event(1))).toBe('duplicate')

    expect(applied).toEqual([1, 2])
  })

  it('buffers events that arrive while reconnecting, then applies them after the resume', () => {
    const { sequencer, applied } = setup(5)
    sequencer.beginResync()

    expect(sequencer.receive(event(8))).toBe('buffered')
    expect(sequencer.receive(event(7))).toBe('buffered')
    sequencer.completeResync(resume(6, 6))

    expect(applied).toEqual([6, 7, 8])
  })

  it('asks for another resume if the buffer still leaves a hole', () => {
    const { sequencer } = setup()
    sequencer.beginResync()
    sequencer.receive(event(5))

    expect(sequencer.completeResync(resume(2, 1, 2))).toBe('gap')
    expect(sequencer.lastSequence).toBe(2)
  })

  it('moves to the latest position without playing anything when replay is off', () => {
    const { sequencer, applied } = setup()
    sequencer.beginResync()

    sequencer.completeResync(resume(40, 38, 39, 40), false)

    expect(applied).toEqual([])
    expect(sequencer.lastSequence).toBe(40)
    expect(sequencer.receive(event(41))).toBe('applied')
  })

  it('jumps past events the server skipped as stale', () => {
    const { sequencer, applied } = setup(10)
    sequencer.beginResync()

    sequencer.completeResync(resume(100, 99, 100))

    expect(applied).toEqual([99, 100])
    expect(sequencer.receive(event(101))).toBe('applied')
  })

  it('trusts the server when it is behind the client, for example after a reset', () => {
    const { sequencer } = setup(500)
    sequencer.beginResync()

    sequencer.completeResync(resume(3))

    expect(sequencer.lastSequence).toBe(3)
    expect(sequencer.receive(event(4))).toBe('applied')
  })
})
