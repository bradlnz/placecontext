import type { RunDashboardChainCommand } from '../model/dashboard'

export interface DashboardEventMap {
  'dashboard.refresh-requested': {
    source: 'dashboard-page' | 'chain-run'
  }
  'dashboard.chain-run-requested': RunDashboardChainCommand
  'dashboard.chain-run-started': {
    chainRunId: string
    message: string
  }
  'dashboard.loaded': {
    runningCount: number
  }
}
