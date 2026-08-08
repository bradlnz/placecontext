import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { analyticsQueryOptions } from '../../api/analytics-query'
import { AnalyticsPage } from './AnalyticsPage'
const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
vi.mock('react-router-dom', () => ({
  useParams: () => ({ projectId }),
  NavLink: ({ children, to, className }: { children: string; to: string; className: string }) => (
    <a className={className} href={to}>
      {children}
    </a>
  ),
}))
vi.mock('./AnalyticsChartCanvas', () => ({
  AnalyticsChartCanvas: ({ name }: { name: string }) => <div aria-label={`${name} chart`} />,
}))
describe('AnalyticsPage', () => {
  it('renders SQL and table charts from context', () => {
    const client = new QueryClient()
    client.setQueryData(analyticsQueryOptions(projectId).queryKey, {
      tables: [{ name: 'roads', rowEstimate: 12 }],
      charts: [
        {
          tableName: 'roads',
          name: 'roads',
          generatedAt: '2026-08-08T00:00:00+00:00',
          generatedAtDisplay: '2026-08-08 10:00',
          spec: { type: 'bar' },
          legacyHtml: null,
          sql: null,
          chartType: 'bar',
        },
      ],
      sweepPending: false,
      pendingTables: [],
    })
    render(
      <QueryClientProvider client={client}>
        <AnalyticsPage />
      </QueryClientProvider>,
    )
    expect(screen.getByRole('heading', { name: 'Analytics' })).toBeVisible()
    expect(screen.getByLabelText('roads chart')).toBeVisible()
    expect(screen.getByText('~12 rows')).toBeVisible()
  })
})
