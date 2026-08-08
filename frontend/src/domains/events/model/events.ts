export interface EventType {
  name: string
  description: string | null
  isBuiltIn: boolean
  payloadSchema: string | null
}

export interface EventOccurrence {
  id: string
  name: string
  source: string
  sourceLabel: string
  payload: string | null
  occurredAt: string
  occurredAtDisplay: string
  triggeredRuns: number
}

export interface EventSubscription {
  id: string
  eventName: string | null
  enabled: boolean
}

export interface EventsPageModel {
  types: EventType[]
  log: EventOccurrence[]
  triggers: EventSubscription[]
}
