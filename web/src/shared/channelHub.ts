import { HttpError, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { EventSequencer } from './sequencer'
import type { EventEnvelope, GoalSnapshot, ResumeResult } from './types'

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'unauthorized'

export interface ChannelHubOptions {
  token: string
  /** Dashboards show recent history on load. A brand new overlay should not play old alerts. */
  replayOnFirstLoad: boolean
  /** When set, the last applied sequence survives a page reload (OBS reloads browser sources on scene changes). */
  storageKey?: string
  onEvent: (event: EventEnvelope) => void
  onGoal: (goal: GoalSnapshot, currency: string) => void
  onStatus: (status: ConnectionStatus) => void
  onOverlays?: (connected: number) => void
}

/** Exponential backoff with jitter, capped at 30 seconds. An overlay runs for hours and must never give up. */
function backoff(attempt: number): number {
  return Math.min(30_000, 1_000 * 2 ** attempt) * (0.8 + Math.random() * 0.4)
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** The client wraps a failed negotiate in its own error type and keeps the HTTP status only in the message. */
function isUnauthorized(error: unknown): boolean {
  if (error instanceof HttpError) return error.statusCode === 401
  return error instanceof Error && error.message.includes("Status code '401'")
}

function readSequence(key?: string): number | null {
  if (!key) return null
  try {
    const value = Number(localStorage.getItem(key))
    return Number.isInteger(value) && value > 0 ? value : null
  } catch {
    return null
  }
}

function writeSequence(key: string | undefined, sequence: number): void {
  if (!key) return
  try {
    localStorage.setItem(key, String(sequence))
  } catch {
    // Storage can be unavailable (private mode, blocked). The overlay still works, it just won't resume across reloads.
  }
}

/** Connects to the channel hub and keeps the client's event stream complete and ordered. Returns a stop function. */
export function startChannelHub(options: ChannelHubOptions): () => Promise<void> {
  const stored = readSequence(options.storageKey)
  let firstLoad = stored === null
  let stopped = false
  let resyncRunning = false
  let resyncAgain = false

  const sequencer = new EventSequencer((event) => {
    options.onEvent(event)
    writeSequence(options.storageKey, event.sequence)
  }, stored ?? 0)

  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/overlay', { accessTokenFactory: () => options.token })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => backoff(ctx.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build()

  async function resync(): Promise<void> {
    if (resyncRunning) {
      resyncAgain = true
      return
    }
    resyncRunning = true
    try {
      do {
        resyncAgain = false
        sequencer.beginResync()
        const result = await connection.invoke<ResumeResult>('Resume', sequencer.lastSequence)
        // Goal first, so buffered live events that follow can move it on.
        options.onGoal(result.goal, result.currency)
        const outcome = sequencer.completeResync(result, !firstLoad || options.replayOnFirstLoad)
        firstLoad = false
        writeSequence(options.storageKey, sequencer.lastSequence)
        options.onOverlays?.(result.connectedOverlays)
        if (outcome === 'gap') resyncAgain = true
      } while (resyncAgain && !stopped)
    } catch {
      // The connection dropped mid-resume; onreconnected starts another. Anything else: try again shortly.
      if (!stopped && connection.state === HubConnectionState.Connected) {
        setTimeout(() => void resync(), backoff(1))
      }
    } finally {
      resyncRunning = false
    }
  }

  connection.on('EventPublished', (event: EventEnvelope) => {
    if (sequencer.receive(event) === 'gap') void resync()
  })
  connection.on('OverlaysChanged', (count: number) => options.onOverlays?.(count))

  connection.onreconnecting(() => {
    options.onStatus('reconnecting')
    sequencer.beginResync()
  })
  connection.onreconnected(() => {
    options.onStatus('connected')
    void resync()
  })
  connection.onclose(() => {
    if (!stopped) void start()
  })

  async function start(): Promise<void> {
    for (let attempt = 0; !stopped; attempt++) {
      try {
        options.onStatus(attempt === 0 ? 'connecting' : 'reconnecting')
        await connection.start()
        options.onStatus('connected')
        await resync()
        return
      } catch (error) {
        if (isUnauthorized(error)) {
          options.onStatus('unauthorized')
          return
        }
        await delay(backoff(attempt))
      }
    }
  }

  void start()

  return async () => {
    stopped = true
    await connection.stop()
  }
}
