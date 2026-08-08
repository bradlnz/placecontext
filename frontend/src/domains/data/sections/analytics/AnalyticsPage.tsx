import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { DataTabs } from '../../../../shared/components/data-tabs/DataTabs'
import { deleteSqlChart, queueAnalyticsRefresh, saveSqlChart } from '../../api/analytics-api'
import { analyticsQueryOptions } from '../../api/analytics-query'
import type { AnalyticsChart } from '../../model/analytics'
import { AnalyticsChartCanvas } from './AnalyticsChartCanvas'

type Command =
  | { kind: 'refresh'; tableName: string | null; instruction: string }
  | { kind: 'save'; name: string; sql: string; chartType: string }
  | { kind: 'delete'; name: string }
export function AnalyticsPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const options = analyticsQueryOptions(projectId)
  const { data } = useSuspenseQuery(options)
  const client = useQueryClient()
  const [editor, setEditor] = useState(false)
  const [name, setName] = useState('')
  const [sql, setSql] = useState('')
  const [chartType, setChartType] = useState('bar')
  const [sqlError, setSqlError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [redrawFor, setRedrawFor] = useState<string | null>(null)
  const [instruction, setInstruction] = useState('')
  const mutation = useMutation({
    mutationFn: async (command: Command) => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'refresh')
        return queueAnalyticsRefresh(projectId, command.tableName, command.instruction, signal)
      if (command.kind === 'save')
        return saveSqlChart(projectId, command.name, command.sql, command.chartType, signal)
      await deleteSqlChart(projectId, command.name, signal)
      return null
    },
    onSuccess: async (_result, command) => {
      if (command.kind === 'save') setEditor(false)
      if (command.kind === 'refresh') {
        setRedrawFor(null)
        setInstruction('')
      }
      await client.invalidateQueries({ queryKey: options.queryKey })
    },
  })
  const sqlCharts = data.charts.filter((chart) => chart.tableName.startsWith('sql:'))
  const chartFor = (table: string): AnalyticsChart | undefined =>
    data.charts.find((chart) => chart.tableName.toLocaleLowerCase() === table.toLocaleLowerCase())
  async function execute(command: Command): Promise<void> {
    setError(null)
    setSqlError(null)
    try {
      await mutation.mutateAsync(command)
    } catch (caught: unknown) {
      const text = caught instanceof Error ? caught.message : 'Analytics could not be updated.'
      if (command.kind === 'save') setSqlError(text)
      else setError(text)
    }
  }
  async function edit(chart: AnalyticsChart): Promise<void> {
    await Promise.resolve()
    setEditor(true)
    setName(chart.name)
    setSql(chart.sql ?? '')
    setChartType(chart.chartType)
  }
  async function toggleEditor(): Promise<void> {
    await Promise.resolve()
    setEditor((value) => !value)
    setSqlError(null)
  }
  return (
    <div className="analytics-page">
      <title>PlaceContext — Analytics</title>
      <DataTabs active="analytics" projectId={projectId} />
      <header className="analytics-head">
        <div>
          <h1>Analytics</h1>
          <p>
            SQL-defined charts plus one auto chart per table — every query runs isolated inside this
            project's own database
          </p>
        </div>
        <button
          className="dcbtn primary"
          disabled={data.sweepPending || mutation.isPending}
          onClick={() => void execute({ kind: 'refresh', tableName: null, instruction: '' })}
          type="button"
        >
          {data.sweepPending ? 'Generating…' : '↻ Generate all'}
        </button>
      </header>
      {error === null ? null : (
        <div className="error-banner" role="alert">
          {error}
        </div>
      )}
      <div className="analytics-section-head">
        <h2>Charts</h2>
        <button className="dcbtn" onClick={() => void toggleEditor()} type="button">
          ＋ SQL chart
        </button>
      </div>
      {editor ? (
        <section className="dccard analytics-sql-editor">
          <input
            className="dcinput"
            onChange={(event) => {
              setName(event.target.value)
            }}
            placeholder="chart name (e.g. sales per city)"
            value={name}
          />
          <textarea
            onChange={(event) => {
              setSql(event.target.value)
            }}
            placeholder="SELECT city, avg(price) FROM listings GROUP BY city ORDER BY 2 DESC"
            rows={4}
            spellCheck={false}
            value={sql}
          />
          <div>
            <select
              className="dcinput"
              onChange={(event) => {
                setChartType(event.target.value)
              }}
              value={chartType}
            >
              <option value="bar">bar</option>
              <option value="line">line</option>
              <option value="pie">pie</option>
            </select>
            <button
              className="dcbtn primary"
              disabled={mutation.isPending}
              onClick={() => void execute({ kind: 'save', name, sql, chartType })}
              type="button"
            >
              {mutation.isPending ? 'Running…' : '▶ Run & save'}
            </button>
            <span>First text column = labels, numeric columns = series.</span>
            {sqlError === null ? null : <strong role="alert">{sqlError}</strong>}
          </div>
        </section>
      ) : null}
      {sqlCharts.length === 0 ? null : (
        <div className="analytics-grid">
          {sqlCharts.map((chart) => (
            <article className="dccard analytics-chart-card" key={chart.tableName}>
              <header>
                <strong>{chart.name}</strong>
                <span>SQL</span>
                <div>
                  {(['bar', 'line', 'pie'] as const).map((type) => (
                    <button
                      className={chart.chartType === type ? 'active' : ''}
                      key={type}
                      onClick={() =>
                        void execute({
                          kind: 'save',
                          name: chart.name,
                          sql: chart.sql ?? '',
                          chartType: type,
                        })
                      }
                      type="button"
                    >
                      {type}
                    </button>
                  ))}
                </div>
                <small>{chart.generatedAtDisplay}</small>
                <button
                  onClick={() =>
                    void execute({
                      kind: 'save',
                      name: chart.name,
                      sql: chart.sql ?? '',
                      chartType: chart.chartType,
                    })
                  }
                  type="button"
                >
                  ↻ refresh
                </button>
                <button onClick={() => void edit(chart)} type="button">
                  edit
                </button>
                <button
                  onClick={() => void execute({ kind: 'delete', name: chart.name })}
                  type="button"
                >
                  ✕
                </button>
              </header>
              {chart.spec === null ? null : (
                <AnalyticsChartCanvas name={chart.name} spec={chart.spec} />
              )}
            </article>
          ))}
        </div>
      )}
      <h2 className="analytics-subtitle">Table charts</h2>
      {data.tables.length === 0 ? (
        <div className="dccard analytics-empty">
          No tables yet — create some on the <a href={`/project/${projectId}/data`}>Data</a> tab and
          charts will be drawn over them here.
        </div>
      ) : (
        <div className="analytics-grid">
          {data.tables.map((table) => {
            const chart = chartFor(table.name)
            const pending = data.pendingTables.includes(table.name)
            return (
              <article className="dccard analytics-chart-card" key={table.name}>
                <header>
                  <strong>{table.name}</strong>
                  <small>~{table.rowEstimate} rows</small>
                  <i />
                  {pending ? (
                    <span>drawing…</span>
                  ) : chart === undefined ? null : (
                    <small>{chart.generatedAtDisplay}</small>
                  )}
                  <button
                    disabled={pending}
                    onClick={() => {
                      setRedrawFor(redrawFor === table.name ? null : table.name)
                      setInstruction('')
                    }}
                    type="button"
                  >
                    {chart === undefined ? '▶ draw' : '↻ redraw'}
                  </button>
                </header>
                {redrawFor === table.name ? (
                  <div className="analytics-redraw">
                    <input
                      onChange={(event) => {
                        setInstruction(event.target.value)
                      }}
                      placeholder="what should this chart show?"
                      value={instruction}
                    />
                    <button
                      onClick={() =>
                        void execute({
                          kind: 'refresh',
                          tableName: table.name,
                          instruction,
                        })
                      }
                      type="button"
                    >
                      Go
                    </button>
                    <button
                      onClick={() => {
                        setRedrawFor(null)
                      }}
                      type="button"
                    >
                      ✕
                    </button>
                  </div>
                ) : null}
                {chart?.spec !== null && chart !== undefined ? (
                  <AnalyticsChartCanvas name={chart.name} spec={chart.spec} />
                ) : chart?.legacyHtml !== null && chart !== undefined ? (
                  <iframe sandbox="" srcDoc={chart.legacyHtml} title={`${chart.name} chart`} />
                ) : (
                  <div className="analytics-chart-empty">
                    {pending
                      ? 'the local LLM is drawing this chart — it appears here when done'
                      : 'no chart yet — hit draw'}
                  </div>
                )}
              </article>
            )
          })}
        </div>
      )}
    </div>
  )
}
