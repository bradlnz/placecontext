import { getJson, postJson } from '../../../shared/api/http-client'
import type { EventsPageModel, EventType } from '../model/events'
import { emittedEvent, eventsPage } from './events-schemas'
import type { z } from 'zod'

const root = (projectId: string) =>
  `/api/v1/projects/${encodeURIComponent(projectId)}/event-page`

export const fetchEvents = (projectId: string, signal: AbortSignal): Promise<EventsPageModel> =>
  getJson({ path: root(projectId), schema: eventsPage, signal })

export const defineEventType = (
  projectId: string,
  body: { name: string; description: string; payloadSchema: string },
  signal: AbortSignal,
): Promise<EventType> =>
  postJson({
    path: `${root(projectId)}/types`,
    body,
    schema: eventsPage.shape.types.element,
    signal,
  })

export const emitEvent = (
  projectId: string,
  name: string,
  payload: string,
  signal: AbortSignal,
): Promise<z.infer<typeof emittedEvent>> =>
  postJson({
    path: `${root(projectId)}/types/${encodeURIComponent(name)}/occurrences`,
    body: { payload },
    schema: emittedEvent,
    signal,
  })
