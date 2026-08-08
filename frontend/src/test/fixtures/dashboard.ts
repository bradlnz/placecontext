import type { Dashboard } from '../../domains/operations/model/dashboard'

export const dashboardFixture: Dashboard = {
  project: {
    id: 'd809bf2d-45b8-4ac0-828e-b0bb10ad296d',
    name: 'Atlas',
  },
  stats: {
    running: 2,
    queued: 1,
    failed24: 3,
    succeeded24: 17,
  },
  chains: [
    {
      id: '57f6f960-1890-45e9-8aa1-150f8d6d0197',
      projectId: 'd809bf2d-45b8-4ac0-828e-b0bb10ad296d',
      name: 'Daily context refresh',
      stageCount: 2,
      jobCount: 2,
      promptSteps: [
        {
          index: 0,
          jobName: 'Index repository',
          parameters: [
            {
              name: 'branch',
              label: 'Branch',
              required: true,
              type: 'text',
              options: [],
              defaultValue: 'main',
            },
          ],
        },
      ],
    },
  ],
  entities: [
    {
      id: '28bec4fa-ec06-45ad-bc66-c5157d90c83c',
      projectId: 'd809bf2d-45b8-4ac0-828e-b0bb10ad296d',
      name: 'Customers',
      tableName: 'customers',
      rowCount: 1280,
      chartColumn: 'region',
      bars: [
        { label: 'Queensland', count: 480, percentage: 100 },
        { label: 'Victoria', count: 240, percentage: 50 },
      ],
    },
  ],
  charts: [
    {
      name: 'Runs by day',
      generatedAt: '2026-08-08T04:30:00.000Z',
      spec: {
        type: 'bar',
        labels: ['Mon', 'Tue'],
        series: [{ name: 'Runs', values: [8, 12] }],
      },
    },
  ],
  recentRuns: [
    {
      id: '50ecbd7f-029f-4c4d-92a2-c1256a4f8ea3',
      jobName: 'Build context',
      projectName: 'Atlas',
      status: 'Running',
      succeededShards: 2,
      failedShards: 0,
      startedAt: '2026-08-08T04:00:00.000Z',
      finishedAt: null,
      sourceKind: 'code',
    },
    {
      id: '248815a9-47cb-498c-8097-5918f26e2608',
      jobName: 'Publish artifacts',
      projectName: 'Atlas',
      status: 'Failed',
      succeededShards: 1,
      failedShards: 1,
      startedAt: '2026-08-08T03:00:00.000Z',
      finishedAt: '2026-08-08T03:01:10.000Z',
      sourceKind: 'image',
    },
  ],
}
