import type { CreatedChannel, EventEnvelope, EventType } from './types'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function readError(response: Response): Promise<ApiError> {
  try {
    const problem = await response.json()
    const details = problem.errors ? Object.values(problem.errors).flat().join(' ') : problem.detail
    return new ApiError(response.status, details || problem.title || response.statusText)
  } catch {
    return new ApiError(response.status, response.statusText)
  }
}

/**
 * Sends a write with one Idempotency-Key for all its attempts. If the first attempt reached the server
 * but the response was lost, the retry returns the original event instead of creating a second one.
 */
async function sendIdempotent<T>(url: string, method: string, key: string, body: unknown): Promise<T> {
  const idempotencyKey = crypto.randomUUID()
  let lastError: unknown

  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      const response = await fetch(url, {
        method,
        headers: {
          Authorization: `Bearer ${key}`,
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
        },
        body: JSON.stringify(body),
      })
      if (response.ok) return (await response.json()) as T
      if (response.status < 500) throw await readError(response)
      lastError = await readError(response)
    } catch (error) {
      if (error instanceof ApiError) throw error
      lastError = error
    }
    await new Promise((resolve) => setTimeout(resolve, 300 * 2 ** attempt))
  }

  throw lastError
}

export async function createChannel(name: string, currency: string): Promise<CreatedChannel> {
  const response = await fetch('/api/channels', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, currency }),
  })
  if (!response.ok) throw await readError(response)
  return response.json()
}

export interface PublishEvent {
  type: Exclude<EventType, 'GoalSet'>
  displayName: string
  amountMinor?: number
  message?: string
}

export const publishEvent = (dashboardKey: string, event: PublishEvent) =>
  sendIdempotent<EventEnvelope>('/api/channel/events', 'POST', dashboardKey, event)

export const setGoal = (dashboardKey: string, title: string, targetMinor: number) =>
  sendIdempotent<EventEnvelope>('/api/channel/goal', 'PUT', dashboardKey, { title, targetMinor })
