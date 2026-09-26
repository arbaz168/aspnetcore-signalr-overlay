import { describe, expect, it } from 'vitest'
import { type Alert, AlertQueue } from './alertQueue'
import type { EventEnvelope, EventType } from './types'

let sequence = 0

function event(type: EventType, amountMinor: number | null = null): EventEnvelope {
  sequence += 1
  return {
    id: `evt-${sequence}`,
    sequence,
    type,
    displayName: `viewer ${sequence}`,
    amountMinor,
    currency: 'GBP',
    message: null,
    createdAt: new Date(0).toISOString(),
    goal: { title: null, targetMinor: 0, currentMinor: 0 },
  }
}

function drain(queue: AlertQueue): Alert[] {
  const alerts: Alert[] = []
  for (let alert = queue.next(); alert; alert = queue.next()) alerts.push(alert)
  return alerts
}

describe('AlertQueue', () => {
  it('plays alerts one at a time in arrival order', () => {
    const queue = new AlertQueue(10)
    const follow = event('Follow')
    const donation = event('Donation', 500)

    queue.enqueue(follow)
    queue.enqueue(donation)

    expect(drain(queue)).toEqual([
      { kind: 'single', event: follow },
      { kind: 'single', event: donation },
    ])
  })

  it('does not show an alert for a goal change', () => {
    const queue = new AlertQueue(10)

    queue.enqueue(event('GoalSet'))

    expect(queue.size).toBe(0)
  })

  it('folds a burst beyond its capacity into one summary at the end', () => {
    const queue = new AlertQueue(3)
    const first = event('Donation', 100)
    const second = event('Donation', 200)

    queue.enqueue(first)
    queue.enqueue(second)
    queue.enqueue(event('Donation', 300))
    for (let i = 0; i < 20; i++) queue.enqueue(event('Donation', 50))

    expect(queue.size).toBe(3)
    expect(drain(queue)).toEqual([
      { kind: 'single', event: first },
      { kind: 'single', event: second },
      { kind: 'summary', count: 21, totalMinor: 300 + 20 * 50, currency: 'GBP' },
    ])
  })

  it('counts follows in a summary without adding to its total', () => {
    const queue = new AlertQueue(1)

    queue.enqueue(event('Follow'))
    queue.enqueue(event('Follow'))
    queue.enqueue(event('Donation', 250))

    expect(drain(queue)).toEqual([{ kind: 'summary', count: 3, totalMinor: 250, currency: 'GBP' }])
  })

  it('starts showing single alerts again once the queue has drained', () => {
    const queue = new AlertQueue(1)
    queue.enqueue(event('Follow'))
    queue.enqueue(event('Follow'))
    drain(queue)

    const next = event('Subscription')
    queue.enqueue(next)

    expect(queue.next()).toEqual({ kind: 'single', event: next })
  })
})
