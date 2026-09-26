import { useEffect, useRef, useState } from 'react'
import { type Alert, AlertQueue } from '../shared/alertQueue'
import { type ConnectionStatus, startChannelHub } from '../shared/channelHub'
import { formatMoney } from '../shared/format'
import type { GoalSnapshot } from '../shared/types'

const DISPLAY_MS = 5_000
const EXIT_MS = 400

function readToken(): string | null {
  return new URLSearchParams(window.location.hash.slice(1)).get('token')
}

export function Overlay() {
  // The token lives in the URL fragment, which browsers never send to the server, so it stays out of access logs.
  const [token] = useState(readToken)
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const [goal, setGoal] = useState<GoalSnapshot | null>(null)
  const [currency, setCurrency] = useState('GBP')
  const [alert, setAlert] = useState<Alert | null>(null)
  const [leaving, setLeaving] = useState(false)
  const [queued, setQueued] = useState(0)
  const queue = useRef(new AlertQueue(10))

  useEffect(() => {
    if (!token) return
    const stop = startChannelHub({
      token,
      replayOnFirstLoad: false,
      storageKey: `live-overlay:last-sequence:${token.slice(-12)}`,
      onEvent: (event) => {
        queue.current.enqueue(event)
        setQueued((n) => n + 1)
        setGoal(event.goal)
        setCurrency(event.currency)
      },
      onGoal: (snapshot, channelCurrency) => {
        setGoal(snapshot)
        setCurrency(channelCurrency)
      },
      onStatus: setStatus,
    })
    return () => void stop()
  }, [token])

  // One alert on screen at a time: show it for a fixed time, animate it out, then take the next.
  // The timer depends only on the alert, so events arriving meanwhile cannot extend it.
  useEffect(() => {
    if (!alert) return
    const exit = setTimeout(() => setLeaving(true), DISPLAY_MS)
    const done = setTimeout(() => {
      setLeaving(false)
      setAlert(null)
    }, DISPLAY_MS + EXIT_MS)
    return () => {
      clearTimeout(exit)
      clearTimeout(done)
    }
  }, [alert])

  useEffect(() => {
    if (alert) return
    const next = queue.current.next()
    if (next) setAlert(next)
  }, [alert, queued])

  if (!token) {
    return <p className="overlay-help">Add the overlay URL from the dashboard, including its #token=… part.</p>
  }

  if (status === 'unauthorized') {
    return <p className="overlay-help">This overlay token is not valid. Copy the overlay URL from the dashboard again.</p>
  }

  return (
    <div className="overlay">
      {alert && <AlertCard alert={alert} leaving={leaving} />}
      {goal?.title && goal.targetMinor > 0 && <GoalBar goal={goal} currency={currency} />}
      {status === 'reconnecting' && <span className="overlay-status" title="Reconnecting" />}
    </div>
  )
}

function AlertCard({ alert, leaving }: { alert: Alert; leaving: boolean }) {
  const className = `alert ${leaving ? 'alert-leave' : 'alert-enter'}`

  if (alert.kind === 'summary') {
    return (
      <div className={className}>
        <div className="alert-title">
          <strong>{alert.count}</strong> more supporters
        </div>
        {alert.totalMinor > 0 && <div className="alert-amount">{formatMoney(alert.totalMinor, alert.currency)} in total</div>}
      </div>
    )
  }

  const { event } = alert
  return (
    <div className={`${className} alert-${event.type.toLowerCase()}`}>
      <div className="alert-title">
        <strong>{event.displayName}</strong>
        {event.type === 'Follow' && ' just followed'}
        {event.type === 'Subscription' && ' subscribed'}
        {event.type === 'Donation' && ' donated'}
      </div>
      {event.amountMinor !== null && <div className="alert-amount">{formatMoney(event.amountMinor, event.currency)}</div>}
      {event.message && <div className="alert-message">{event.message}</div>}
    </div>
  )
}

function GoalBar({ goal, currency }: { goal: GoalSnapshot; currency: string }) {
  const percent = Math.min(100, (goal.currentMinor / goal.targetMinor) * 100)

  return (
    <div className="goal">
      <div className="goal-label">
        <span>{goal.title}</span>
        <span>
          {formatMoney(goal.currentMinor, currency)} / {formatMoney(goal.targetMinor, currency)}
        </span>
      </div>
      <div className="goal-track" role="progressbar" aria-valuenow={Math.round(percent)} aria-valuemin={0} aria-valuemax={100}>
        <div className="goal-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}
