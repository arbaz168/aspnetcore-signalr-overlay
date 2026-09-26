import { type FormEvent, useEffect, useState } from 'react'
import { type PublishEvent, createChannel, publishEvent, setGoal } from '../shared/api'
import { type ConnectionStatus, startChannelHub } from '../shared/channelHub'
import { formatMoney, toMinor } from '../shared/format'
import type { CreatedChannel, EventEnvelope, GoalSnapshot } from '../shared/types'

const STORAGE_KEY = 'live-overlay:channel'
const FEED_SIZE = 40
const NAMES = ['Ayesha', 'Bilal', 'Chen', 'Dana', 'Emeka', 'Farah', 'Gus', 'Hana', 'Idris', 'Jonas', 'Kavya', 'Leo']

const randomName = () => NAMES[Math.floor(Math.random() * NAMES.length)]

function loadChannel(): CreatedChannel | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as CreatedChannel) : null
  } catch {
    return null
  }
}

function saveChannel(channel: CreatedChannel | null): void {
  try {
    if (channel) localStorage.setItem(STORAGE_KEY, JSON.stringify(channel))
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    // Without storage the channel lasts for this tab only.
  }
}

export function Dashboard() {
  const [channel, setChannel] = useState(loadChannel)

  const use = (created: CreatedChannel | null) => {
    saveChannel(created)
    setChannel(created)
  }

  return (
    <main className="page">
      <header className="page-header">
        <h1>Live Overlay</h1>
        <p>Real-time stream alerts over ASP.NET Core SignalR.</p>
      </header>
      {channel ? <ChannelDashboard key={channel.id} channel={channel} onForget={() => use(null)} /> : <CreateChannel onCreated={use} />}
    </main>
  )
}

function CreateChannel({ onCreated }: { onCreated: (channel: CreatedChannel) => void }) {
  const [name, setName] = useState('My stream')
  const [currency, setCurrency] = useState('GBP')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      onCreated(await createChannel(name, currency))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create the channel.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card narrow">
      <h2>Create a channel</h2>
      <p className="muted">You get a dashboard key to send events and a read-only overlay URL for OBS.</p>
      <form onSubmit={submit} className="stack">
        <label>
          Channel name
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={60} required />
        </label>
        <label>
          Currency
          <select value={currency} onChange={(e) => setCurrency(e.target.value)}>
            <option>GBP</option>
            <option>USD</option>
            <option>EUR</option>
            <option>JPY</option>
          </select>
        </label>
        {error && <p className="error">{error}</p>}
        <button type="submit" disabled={busy}>
          {busy ? 'Creating…' : 'Create channel'}
        </button>
      </form>
    </section>
  )
}

function ChannelDashboard({ channel, onForget }: { channel: CreatedChannel; onForget: () => void }) {
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const [overlays, setOverlays] = useState(0)
  const [goal, setGoalState] = useState<GoalSnapshot | null>(null)
  const [feed, setFeed] = useState<EventEnvelope[]>([])
  const overlayUrl = `${window.location.origin}/overlay.html#token=${channel.overlayToken}`

  useEffect(() => {
    const stop = startChannelHub({
      token: channel.dashboardKey,
      replayOnFirstLoad: true,
      onEvent: (event) => {
        setFeed((items) => [event, ...items].slice(0, FEED_SIZE))
        setGoalState(event.goal)
      },
      onGoal: (snapshot) => setGoalState(snapshot),
      onStatus: setStatus,
      onOverlays: setOverlays,
    })
    return () => void stop()
  }, [channel.dashboardKey])

  return (
    <div className="grid">
      <section className="card span-2">
        <div className="row between">
          <div>
            <h2>{channel.name}</h2>
            <p className="muted">
              <StatusDot status={status} /> {statusText(status)} · {overlays} overlay{overlays === 1 ? '' : 's'} connected
            </p>
          </div>
          <button className="link" onClick={onForget}>
            Forget channel
          </button>
        </div>
        <label>
          Overlay URL, add it to OBS as a browser source (1920 × 1080)
          <div className="row">
            <input readOnly value={overlayUrl} onFocus={(e) => e.target.select()} />
            <button type="button" onClick={() => navigator.clipboard?.writeText(overlayUrl)}>
              Copy
            </button>
            <a className="button secondary" href={overlayUrl} target="_blank" rel="noreferrer">
              Open
            </a>
          </div>
        </label>
      </section>

      <SendEvents channel={channel} />
      <GoalForm channel={channel} goal={goal} />

      <section className="card span-2">
        <h3>Activity</h3>
        {feed.length === 0 ? (
          <p className="muted">No events yet. Send one above.</p>
        ) : (
          <ol className="feed">
            {feed.map((event) => (
              <li key={event.id}>
                <span className="seq">#{event.sequence}</span>
                <span className={`tag tag-${event.type.toLowerCase()}`}>{event.type}</span>
                <span className="grow">{describe(event)}</span>
                <time className="muted">{new Date(event.createdAt).toLocaleTimeString()}</time>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  )
}

function SendEvents({ channel }: { channel: CreatedChannel }) {
  const [name, setName] = useState(randomName)
  const [amount, setAmount] = useState('5.00')
  const [message, setMessage] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      setName(randomName())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Request failed.')
    } finally {
      setBusy(false)
    }
  }

  const send = (event: PublishEvent) => run(() => publishEvent(channel.dashboardKey, event))

  const donate = () => {
    const amountMinor = toMinor(amount, channel.currency)
    if (amountMinor === null) {
      setError('Enter an amount above zero.')
      return
    }
    void send({ type: 'Donation', displayName: name, amountMinor, message: message || undefined })
  }

  // Twenty donations at once: concurrent publishers on the server, and the overlay folding the burst into a summary.
  const burst = () =>
    run(() =>
      Promise.all(
        Array.from({ length: 20 }, () =>
          publishEvent(channel.dashboardKey, {
            type: 'Donation',
            displayName: randomName(),
            amountMinor: toMinor(String(1 + Math.floor(Math.random() * 20)), channel.currency) ?? 100,
          }),
        ),
      ),
    )

  return (
    <section className="card">
      <h3>Send a test event</h3>
      <div className="stack">
        <label>
          Viewer name
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={40} />
        </label>
        <label>
          Amount ({channel.currency})
          <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" />
        </label>
        <label>
          Message
          <input value={message} onChange={(e) => setMessage(e.target.value)} maxLength={200} placeholder="Optional" />
        </label>
        {error && <p className="error">{error}</p>}
        <div className="row wrap">
          <button disabled={busy} onClick={donate}>
            Donate
          </button>
          <button disabled={busy} className="secondary" onClick={() => void send({ type: 'Follow', displayName: name })}>
            Follow
          </button>
          <button disabled={busy} className="secondary" onClick={() => void send({ type: 'Subscription', displayName: name })}>
            Subscribe
          </button>
          <button disabled={busy} className="secondary" onClick={() => void burst()}>
            Burst × 20
          </button>
        </div>
      </div>
    </section>
  )
}

function GoalForm({ channel, goal }: { channel: CreatedChannel; goal: GoalSnapshot | null }) {
  const [title, setTitle] = useState('New microphone')
  const [target, setTarget] = useState('100')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const targetMinor = toMinor(target, channel.currency)
    if (targetMinor === null) {
      setError('Enter a target above zero.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await setGoal(channel.dashboardKey, title, targetMinor)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not set the goal.')
    } finally {
      setBusy(false)
    }
  }

  const percent = goal && goal.targetMinor > 0 ? Math.min(100, (goal.currentMinor / goal.targetMinor) * 100) : 0

  return (
    <section className="card">
      <h3>Goal</h3>
      {goal?.title ? (
        <div className="goal-preview">
          <div className="row between">
            <strong>{goal.title}</strong>
            <span>
              {formatMoney(goal.currentMinor, channel.currency)} / {formatMoney(goal.targetMinor, channel.currency)}
            </span>
          </div>
          <div className="bar">
            <div style={{ width: `${percent}%` }} />
          </div>
        </div>
      ) : (
        <p className="muted">No goal set.</p>
      )}
      <form onSubmit={submit} className="stack">
        <label>
          Title
          <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={60} required />
        </label>
        <label>
          Target ({channel.currency})
          <input value={target} onChange={(e) => setTarget(e.target.value)} inputMode="decimal" required />
        </label>
        {error && <p className="error">{error}</p>}
        <button type="submit" disabled={busy}>
          Set new goal
        </button>
      </form>
    </section>
  )
}

function StatusDot({ status }: { status: ConnectionStatus }) {
  return <span className={`dot dot-${status}`} aria-hidden />
}

function statusText(status: ConnectionStatus): string {
  switch (status) {
    case 'connected':
      return 'Live'
    case 'connecting':
      return 'Connecting'
    case 'reconnecting':
      return 'Reconnecting'
    case 'unauthorized':
      return 'Key rejected. Forget this channel and create a new one'
  }
}

function describe(event: EventEnvelope): string {
  const amount = event.amountMinor !== null ? ` ${formatMoney(event.amountMinor, event.currency)}` : ''
  switch (event.type) {
    case 'Donation':
      return `${event.displayName} donated${amount}${event.message ? `: "${event.message}"` : ''}`
    case 'Follow':
      return `${event.displayName} followed`
    case 'Subscription':
      return `${event.displayName} subscribed`
    case 'GoalSet':
      return `New goal: ${event.displayName} (${formatMoney(event.goal.targetMinor, event.currency)})`
  }
}
