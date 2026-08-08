import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  deleteSqlChart,
  fetchAnalytics,
  queueAnalyticsRefresh,
  saveSqlChart,
} from './analytics-api'
const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const chart = {
  tableName: 'sql:sales',
  name: 'sales',
  generatedAt: '2026-08-08T00:00:00+00:00',
  generatedAtDisplay: '2026-08-08 10:00',
  spec: { type: 'bar' },
  legacyHtml: null,
  sql: 'select 1',
  chartType: 'bar',
}
describe('Analytics API', () => {
  afterEach(() => vi.restoreAllMocks())
  it('supports context, refresh, save, and delete routes', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => {
      if (init?.method === 'DELETE') return Promise.resolve(new Response(null, { status: 204 }))
      const body =
        init?.method === 'POST'
          ? { message: 'queued' }
          : init?.method === 'PUT'
            ? chart
            : {
                tables: [],
                charts: [],
                sweepPending: false,
                pendingTables: [],
              }
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }))
    })
    const signal = new AbortController().signal
    await fetchAnalytics(projectId, signal)
    await queueAnalyticsRefresh(projectId, null, '', signal)
    await saveSqlChart(projectId, 'sales', 'select 1', 'bar', signal)
    await deleteSqlChart(projectId, 'sales', signal)
    expect(fetchMock).toHaveBeenCalledTimes(4)
  })
})
