import { afterEach, describe, expect, it, vi } from 'vitest'
import { createSchedule, deleteSchedule, fetchSchedules, updateSchedule } from './schedules-api'
const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const item = {
  id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
  name: 'Nightly',
  kind: 'Schedule',
  enabled: true,
  cronExpression: '0 9 * * *',
  eventName: null,
  jobId: projectId,
  chainId: null,
  sourceTable: null,
  prompt: null,
  targetLabel: 'Import',
  nextRunLabel: 'Aug 9 · 09:00',
  lastFiredLabel: 'never',
}
describe('Schedules API', () => {
  afterEach(() => vi.restoreAllMocks())
  it('supports page CRUD routes', async () => {
    const mock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) =>
      init?.method === 'DELETE'
        ? Promise.resolve(new Response(null, { status: 204 }))
        : Promise.resolve(
            new Response(
              JSON.stringify(
                init?.method === 'GET'
                  ? {
                      timeZoneId: 'Australia/Brisbane',
                      jobs: [],
                      chains: [],
                      tables: [],
                      eventTypes: [],
                      triggers: [item],
                    }
                  : item,
              ),
              { status: 200 },
            ),
          ),
    )
    const signal = new AbortController().signal
    await fetchSchedules(projectId, signal)
    await createSchedule(projectId, {}, signal)
    await updateSchedule(projectId, item.id, {}, signal)
    await deleteSchedule(projectId, item.id, signal)
    expect(mock).toHaveBeenCalledTimes(4)
  })
})
