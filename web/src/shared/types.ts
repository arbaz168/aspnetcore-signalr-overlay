export type EventType = 'Follow' | 'Subscription' | 'Donation' | 'GoalSet'

export interface GoalSnapshot {
  title: string | null
  targetMinor: number
  currentMinor: number
}

export interface EventEnvelope {
  id: string
  sequence: number
  type: EventType
  displayName: string
  amountMinor: number | null
  currency: string
  message: string | null
  createdAt: string
  goal: GoalSnapshot
}

export interface ResumeResult {
  events: EventEnvelope[]
  latestSequence: number
  skippedCount: number
  currency: string
  goal: GoalSnapshot
  connectedOverlays: number
}

export interface CreatedChannel {
  id: string
  name: string
  currency: string
  dashboardKey: string
  overlayToken: string
}
