import { createContext, use, useState, type ReactNode } from 'react'

import type { DashboardEventMap } from '../domains/operations/events/dashboard-events'
import type { SettingsEventMap } from '../domains/settings/events/settings-events'
import type { WorkspaceEventMap } from '../domains/workspace/events/workspace-events'
import { AsyncEventBus } from '../shared/events/async-event-bus'

type AppEventMap = WorkspaceEventMap & DashboardEventMap & SettingsEventMap

const AppEventBusContext = createContext<AsyncEventBus<AppEventMap> | null>(null)

interface AppEventBusProviderProps {
  children: ReactNode
}

export function AppEventBusProvider({ children }: AppEventBusProviderProps) {
  const [eventBus] = useState(() => new AsyncEventBus<AppEventMap>())

  return <AppEventBusContext value={eventBus}>{children}</AppEventBusContext>
}

export function useAppEventBus(): AsyncEventBus<AppEventMap> {
  const eventBus = use(AppEventBusContext)

  if (eventBus === null) {
    throw new Error('useAppEventBus must be used within AppEventBusProvider.')
  }

  return eventBus
}
