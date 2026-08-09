import { useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'

import {
  deleteOpenSearchDashboard,
  saveOpenSearchDashboard,
  searchOpenSearch,
  triggerOpenSearchSync,
} from '../../api/open-search-api'
import {
  generatedOpenSearchChartsQueryOptions,
  openSearchPageQueryOptions,
  openSearchResultQueryOptions,
} from '../../api/open-search-query'
import type {
  OpenSearchDashboard,
  OpenSearchHit,
  OpenSearchRequest,
  SaveOpenSearchDashboardRequest,
} from '../../model/open-search'
import { DataTabs } from '../../../../shared/components/data-tabs/DataTabs'
import { OpenSearchChart } from './OpenSearchChart'

const pageSize = 25

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'The request could not be completed.'
}

function requestForDashboard(dashboard: OpenSearchDashboard): OpenSearchRequest {
  return {
    indexPattern: dashboard.indexPattern,
    queryText: dashboard.queryText,
    page: 1,
    pageSize,
    bucketField: dashboard.bucketField,
    bucketType: dashboard.bucketType,
    chartType: dashboard.chartType,
    metricType: dashboard.metricType,
    metricField: dashboard.metricField,
    dateInterval: dashboard.dateInterval,
  }
}

function sharedValue(source: OpenSearchHit, target: OpenSearchHit): boolean {
  return Object.entries(source.fields).some(
    ([field, value]) => value !== null && value !== '' && target.fields[field] === value,
  )
}

export function DataSearchPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const url = new URLSearchParams(window.location.search)
  const requestedIndex = url.get('index') ?? ''
  const requestedQuery = url.get('q') ?? ''
  const requestedDocument = url.get('document')
  const [selectedIndex, setSelectedIndex] = useState(requestedIndex)
  const { data } = useSuspenseQuery(openSearchPageQueryOptions(projectId, selectedIndex))
  const indexPattern = selectedIndex === '' ? data.selectedIndex : selectedIndex
  const [queryText, setQueryText] = useState(requestedQuery)
  const [bucketField, setBucketField] = useState('')
  const [chartType, setChartType] = useState('bar')
  const [metricType, setMetricType] = useState('count')
  const [metricField, setMetricField] = useState('')
  const [dateInterval, setDateInterval] = useState('day')
  const [searchRequest, setSearchRequest] = useState<OpenSearchRequest | null>(null)
  const [insightQuery, setInsightQuery] = useState(requestedQuery)
  const resultQuery = useQuery(openSearchResultQueryOptions(projectId, searchRequest))
  const generatedQuery = useQuery(
    generatedOpenSearchChartsQueryOptions(projectId, indexPattern, insightQuery, data.fields),
  )
  const queryClient = useQueryClient()
  const [selectedRecord, setSelectedRecord] = useState<OpenSearchHit | null>(null)
  const [dashboardName, setDashboardName] = useState('')
  const [editingDashboardId, setEditingDashboardId] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [notice, setNotice] = useState<{ text: string; error: boolean } | null>(null)
  const result = resultQuery.data ?? null
  const requiresMetric = metricType !== 'count'
  const bucketType = data.fields
    .find((field) => field.name === bucketField)
    ?.type.startsWith('date')
    ? 'date_histogram'
    : 'terms'
  const numericFields = data.fields.filter(
    (field) =>
      field.aggregatable && /byte|short|integer|long|float|double|scaled_float/.test(field.type),
  )
  const visibleColumns = useMemo(
    () =>
      Array.from(new Set(result?.hits.flatMap((hit) => Object.keys(hit.fields)) ?? [])).slice(
        0,
        10,
      ),
    [result],
  )
  const linkedRecords = useMemo(
    () =>
      selectedRecord === null || result === null
        ? []
        : result.hits
            .filter((hit) => hit.id !== selectedRecord.id && sharedValue(selectedRecord, hit))
            .slice(0, 6),
    [result, selectedRecord],
  )

  function buildRequest(page = 1): OpenSearchRequest {
    return {
      indexPattern,
      queryText: queryText.trim() === '' ? null : queryText.trim(),
      page,
      pageSize,
      bucketField: bucketField === '' ? null : bucketField,
      bucketType,
      chartType,
      metricType,
      metricField: requiresMetric && metricField !== '' ? metricField : null,
      dateInterval: bucketType === 'date_histogram' ? dateInterval : null,
    }
  }

  function runSearch(page = 1): void {
    setSelectedRecord(null)
    setInsightQuery(queryText)
    setSearchRequest(buildRequest(page))
  }

  async function refreshPage(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: ['open-search-page', projectId] })
  }

  function dashboardPayload(
    name: string,
    chartSpecJson: string,
    dashboardId: string | null,
    request = buildRequest(),
  ): SaveOpenSearchDashboardRequest {
    return {
      name,
      indexPattern: request.indexPattern,
      queryText: request.queryText,
      bucketField: request.bucketField ?? '',
      bucketType: request.bucketType,
      chartType: request.chartType,
      metricType: request.metricType,
      metricField: request.metricField,
      dateInterval: request.dateInterval,
      chartSpecJson,
      dashboardId,
    }
  }

  async function saveDashboard(): Promise<void> {
    if (
      result?.chartSpecJson === null ||
      result?.chartSpecJson === undefined ||
      dashboardName.trim() === ''
    )
      return
    setBusy('save')
    try {
      await saveOpenSearchDashboard(
        projectId,
        dashboardPayload(dashboardName.trim(), result.chartSpecJson, editingDashboardId),
        AbortSignal.timeout(30_000),
      )
      await refreshPage()
      setNotice({ text: `Saved chart “${dashboardName.trim()}”.`, error: false })
    } catch (error: unknown) {
      setNotice({ text: message(error), error: true })
    } finally {
      setBusy(null)
    }
  }

  function editDashboard(dashboard: OpenSearchDashboard): void {
    setEditingDashboardId(dashboard.id)
    setDashboardName(dashboard.name)
    setSelectedIndex(dashboard.indexPattern)
    setQueryText(dashboard.queryText ?? '')
    setBucketField(dashboard.bucketField)
    setChartType(dashboard.chartType)
    setMetricType(dashboard.metricType)
    setMetricField(dashboard.metricField ?? '')
    setDateInterval(dashboard.dateInterval ?? 'day')
    setSearchRequest(requestForDashboard(dashboard))
  }

  async function duplicateDashboard(dashboard: OpenSearchDashboard): Promise<void> {
    const baseName = `${dashboard.name} copy`
    let name = baseName
    let suffix = 2
    while (
      data.dashboards.some((item) => item.name.toLocaleLowerCase() === name.toLocaleLowerCase())
    ) {
      name = `${baseName} ${String(suffix)}`
      suffix += 1
    }
    setBusy(`duplicate:${dashboard.id}`)
    try {
      await saveOpenSearchDashboard(
        projectId,
        dashboardPayload(name, dashboard.chartSpecJson, null, requestForDashboard(dashboard)),
        AbortSignal.timeout(30_000),
      )
      await refreshPage()
    } catch (error: unknown) {
      setNotice({ text: message(error), error: true })
    } finally {
      setBusy(null)
    }
  }

  async function refreshDashboard(dashboard: OpenSearchDashboard): Promise<void> {
    setBusy(`refresh:${dashboard.id}`)
    try {
      const refreshed = await searchOpenSearch(
        projectId,
        requestForDashboard(dashboard),
        AbortSignal.timeout(60_000),
      )
      if (refreshed.chartSpecJson === null)
        throw new Error('The refreshed query returned no chart buckets.')
      await saveOpenSearchDashboard(
        projectId,
        dashboardPayload(
          dashboard.name,
          refreshed.chartSpecJson,
          dashboard.id,
          requestForDashboard(dashboard),
        ),
        AbortSignal.timeout(30_000),
      )
      await refreshPage()
    } catch (error: unknown) {
      setNotice({ text: message(error), error: true })
    } finally {
      setBusy(null)
    }
  }

  async function removeDashboard(dashboardId: string): Promise<void> {
    try {
      await deleteOpenSearchDashboard(projectId, dashboardId, AbortSignal.timeout(30_000))
      await refreshPage()
    } catch (error: unknown) {
      setNotice({ text: message(error), error: true })
    }
  }

  async function sync(): Promise<void> {
    setBusy('sync')
    try {
      const response = await triggerOpenSearchSync(projectId, AbortSignal.timeout(30_000))
      setNotice({ text: response.message, error: false })
    } catch (error: unknown) {
      setNotice({ text: message(error), error: true })
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="data-search-page-react">
      <title>PlaceContext — Data Search</title>
      <DataTabs active="search" projectId={projectId} />
      <header className="data-search-head">
        <div>
          <h1>Data Search</h1>
          <p>
            Search OpenSearch documents, explore fields, and turn aggregations into reusable charts.
          </p>
        </div>
        <div>
          {data.canSync ? (
            <button
              className="dcbtn"
              disabled={busy === 'sync'}
              onClick={() => {
                void sync()
              }}
              type="button"
            >
              {busy === 'sync' ? 'Starting sync…' : 'Sync now'}
            </button>
          ) : null}
          <span className="search-proxy-chip">● server proxy</span>
        </div>
      </header>
      {notice === null ? null : (
        <div className={notice.error ? 'status-message error' : 'status-message'} role="status">
          {notice.text}
        </div>
      )}
      {data.error === null ? null : (
        <div className="data-search-error">
          <strong>OpenSearch unavailable</strong>
          <span>{data.error}</span>
          <small>Configure the OpenSearch connection in this project’s Vault.</small>
        </div>
      )}

      {data.dashboards.length === 0 ? null : (
        <section className="data-search-section">
          <header>
            <div>
              <strong>Dashboard</strong>
              <span>{data.dashboards.length} saved charts</span>
            </div>
          </header>
          <div className="data-search-chart-grid">
            {data.dashboards.map((dashboard) => (
              <article className="dccard data-search-chart" key={dashboard.id}>
                <header>
                  <div>
                    <strong>{dashboard.name}</strong>
                    <span>
                      {dashboard.indexPattern} · {dashboard.metricType} by {dashboard.bucketField}
                    </span>
                  </div>
                  <div>
                    <button
                      aria-label={`Edit ${dashboard.name}`}
                      onClick={() => {
                        editDashboard(dashboard)
                      }}
                      type="button"
                    >
                      ✎
                    </button>
                    <button
                      aria-label={`Duplicate ${dashboard.name}`}
                      disabled={busy === `duplicate:${dashboard.id}`}
                      onClick={() => {
                        void duplicateDashboard(dashboard)
                      }}
                      type="button"
                    >
                      ⧉
                    </button>
                    <button
                      aria-label={`Refresh ${dashboard.name}`}
                      disabled={busy === `refresh:${dashboard.id}`}
                      onClick={() => {
                        void refreshDashboard(dashboard)
                      }}
                      type="button"
                    >
                      ↻
                    </button>
                    <button
                      aria-label={`Delete ${dashboard.name}`}
                      onClick={() => {
                        void removeDashboard(dashboard.id)
                      }}
                      type="button"
                    >
                      ×
                    </button>
                  </div>
                </header>
                <div>
                  <OpenSearchChart specJson={dashboard.chartSpecJson} />
                </div>
              </article>
            ))}
          </div>
        </section>
      )}

      {data.indices.length === 0 ? null : (
        <section className="data-search-section">
          <header>
            <div>
              <strong>Indexes</strong>
              <span>{data.indices.length} available</span>
            </div>
            <span>
              {data.lastUpdated?.value === null || data.lastUpdated?.value === undefined
                ? 'Last updated unavailable'
                : `Last updated ${new Date(data.lastUpdated.value).toLocaleString()}`}
            </span>
          </header>
          <div className="data-search-index-grid">
            {data.indices.map((index) => (
              <button
                className={index.name === indexPattern ? 'dccard selected' : 'dccard'}
                key={index.name}
                onClick={() => {
                  setSelectedIndex(index.name)
                  setSearchRequest(null)
                  setSelectedRecord(null)
                }}
                type="button"
              >
                <code>{index.name}</code>
                <span>
                  <strong>{index.documentCount.toLocaleString()}</strong> documents
                </span>
                <small>{index.storeSize ?? 'size unavailable'}</small>
              </button>
            ))}
          </div>
        </section>
      )}

      {data.fields.length === 0 ? null : (
        <section className="data-search-section">
          <header>
            <div>
              <strong>Generated insights</strong>
              <span>
                {generatedQuery.isFetching
                  ? 'analysing fields…'
                  : `${String(generatedQuery.data?.length ?? 0)} charts from ${indexPattern}`}
              </span>
            </div>
            <button
              className="dcbtn"
              onClick={() => {
                void generatedQuery.refetch()
              }}
              type="button"
            >
              Regenerate charts
            </button>
          </header>
          {generatedQuery.data === undefined || generatedQuery.data.length === 0 ? (
            <div className="dccard data-search-empty">
              {generatedQuery.isFetching
                ? 'Finding useful date, category, and numeric fields…'
                : 'No chartable values were found.'}
            </div>
          ) : (
            <div className="data-search-chart-grid">
              {generatedQuery.data.map((chart) => (
                <article className="dccard data-search-chart" key={chart.id}>
                  <header>
                    <div>
                      <strong>{chart.title}</strong>
                      <span>{chart.subtitle}</span>
                    </div>
                  </header>
                  <div>
                    <OpenSearchChart specJson={chart.chartSpecJson} />
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>
      )}

      <section className="dccard data-search-builder">
        <div className="data-search-query-row">
          <label>
            Index
            <input
              className="dcinput"
              onChange={(event) => {
                setSelectedIndex(event.target.value)
              }}
              value={indexPattern}
            />
          </label>
          <label>
            Query
            <input
              className="dcinput"
              onChange={(event) => {
                setQueryText(event.target.value)
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter') runSearch()
              }}
              placeholder="Search words or phrases across all fields"
              value={queryText}
            />
          </label>
          <button
            className="dcbtn primary"
            disabled={resultQuery.isFetching}
            onClick={() => {
              runSearch()
            }}
            type="button"
          >
            {resultQuery.isFetching ? 'Searching…' : 'Search'}
          </button>
        </div>
        <p>
          Free-text searches run across all searchable fields. Blank searches match all documents.
        </p>
        <div className="data-search-chart-controls">
          <label>
            Group by
            <select
              className="dcinput"
              onChange={(event) => {
                setBucketField(event.target.value)
              }}
              value={bucketField}
            >
              <option value="">No chart</option>
              {data.fields
                .filter((field) => field.aggregatable)
                .map((field) => (
                  <option key={field.name} value={field.name}>
                    {field.name} ({field.type})
                  </option>
                ))}
            </select>
          </label>
          {bucketField === '' ? null : (
            <>
              <label>
                Chart
                <select
                  className="dcinput"
                  onChange={(event) => {
                    setChartType(event.target.value)
                  }}
                  value={chartType}
                >
                  <option value="bar">Bar</option>
                  <option value="line">Line</option>
                  <option value="pie">Pie</option>
                </select>
              </label>
              <label>
                Metric
                <select
                  className="dcinput"
                  onChange={(event) => {
                    setMetricType(event.target.value)
                  }}
                  value={metricType}
                >
                  <option value="count">Document count</option>
                  <option value="sum">Sum</option>
                  <option value="avg">Average</option>
                  <option value="min">Minimum</option>
                  <option value="max">Maximum</option>
                </select>
              </label>
              {requiresMetric ? (
                <label>
                  Numeric field
                  <select
                    className="dcinput"
                    onChange={(event) => {
                      setMetricField(event.target.value)
                    }}
                    value={metricField}
                  >
                    <option value="">Select a field</option>
                    {numericFields.map((field) => (
                      <option key={field.name}>{field.name}</option>
                    ))}
                  </select>
                </label>
              ) : null}
              {bucketType === 'date_histogram' ? (
                <label>
                  Interval
                  <select
                    className="dcinput"
                    onChange={(event) => {
                      setDateInterval(event.target.value)
                    }}
                    value={dateInterval}
                  >
                    <option value="hour">Hour</option>
                    <option value="day">Day</option>
                    <option value="week">Week</option>
                    <option value="month">Month</option>
                  </select>
                </label>
              ) : null}
            </>
          )}
        </div>
      </section>

      {resultQuery.error === null ? null : (
        <div className="status-message error" role="alert">
          {message(resultQuery.error)}
        </div>
      )}
      {result === null ? null : (
        <>
          <div className="data-search-summary">
            <span>
              <strong>{result.total.toLocaleString()}</strong> documents
            </span>
            <span>{result.tookMs} ms</span>
            <span>page {searchRequest?.page ?? 1}</span>
          </div>
          {result.chartSpecJson === null ? null : (
            <section className="dccard data-search-preview">
              <header>
                <div>
                  <strong>Chart preview</strong>
                  <span>
                    {metricType} by {bucketField}
                  </span>
                </div>
                <div>
                  <input
                    className="dcinput"
                    onChange={(event) => {
                      setDashboardName(event.target.value)
                    }}
                    placeholder="Dashboard chart name"
                    value={dashboardName}
                  />
                  <button
                    className="dcbtn primary"
                    disabled={busy === 'save'}
                    onClick={() => {
                      void saveDashboard()
                    }}
                    type="button"
                  >
                    {busy === 'save'
                      ? 'Saving…'
                      : editingDashboardId === null
                        ? 'Save chart'
                        : 'Update chart'}
                  </button>
                </div>
              </header>
              <div>
                <OpenSearchChart specJson={result.chartSpecJson} />
              </div>
            </section>
          )}
          <section className="dccard data-search-results">
            {result.hits.length === 0 ? (
              <div className="data-search-empty">No documents matched this query.</div>
            ) : (
              <>
                <div>
                  <table>
                    <thead>
                      <tr>
                        <th>_index</th>
                        <th>_id</th>
                        {visibleColumns.map((column) => (
                          <th key={column}>{column}</th>
                        ))}
                        <th>Details</th>
                      </tr>
                    </thead>
                    <tbody>
                      {result.hits.map((hit) => (
                        <tr
                          className={hit.id === requestedDocument ? 'linked' : ''}
                          key={`${hit.index}:${hit.id}`}
                        >
                          <td>
                            <code>{hit.index}</code>
                          </td>
                          <td>
                            <code>{hit.id}</code>
                          </td>
                          {visibleColumns.map((column) => (
                            <td key={column} title={hit.fields[column] ?? ''}>
                              {hit.fields[column] ?? '—'}
                            </td>
                          ))}
                          <td>
                            <button
                              className="dcbtn"
                              onClick={() => {
                                setSelectedRecord(hit)
                              }}
                              type="button"
                            >
                              Inspect
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <footer>
                  <button
                    className="dcbtn"
                    disabled={(searchRequest?.page ?? 1) <= 1 || resultQuery.isFetching}
                    onClick={() => {
                      runSearch(Math.max(1, (searchRequest?.page ?? 1) - 1))
                    }}
                    type="button"
                  >
                    ← Previous
                  </button>
                  <span>Page {searchRequest?.page ?? 1}</span>
                  <button
                    className="dcbtn"
                    disabled={result.hits.length < pageSize || resultQuery.isFetching}
                    onClick={() => {
                      runSearch((searchRequest?.page ?? 1) + 1)
                    }}
                    type="button"
                  >
                    Next →
                  </button>
                </footer>
              </>
            )}
          </section>
        </>
      )}

      {selectedRecord === null ? null : (
        <aside className="data-search-record-panel">
          <header>
            <div>
              <strong>Record details</strong>
              <span>{selectedRecord.index}</span>
            </div>
            <button
              aria-label="Close record details"
              onClick={() => {
                setSelectedRecord(null)
              }}
              type="button"
            >
              ×
            </button>
          </header>
          <div>
            <section>
              <h2>Record</h2>
              <dl>
                <div>
                  <dt>_index</dt>
                  <dd>
                    <code>{selectedRecord.index}</code>
                  </dd>
                </div>
                <div>
                  <dt>_id</dt>
                  <dd>
                    <code>{selectedRecord.id}</code>
                  </dd>
                </div>
                {Object.entries(selectedRecord.fields)
                  .sort(([left], [right]) => left.localeCompare(right))
                  .map(([field, value]) => (
                    <div key={field}>
                      <dt>{field}</dt>
                      <dd>{value ?? '—'}</dd>
                    </div>
                  ))}
              </dl>
            </section>
            <section>
              <h2>Linked records</h2>
              {linkedRecords.length === 0 ? (
                <p>No obvious linked records on this page.</p>
              ) : (
                linkedRecords.map((hit) => (
                  <button
                    className="dcbtn data-linked-hit"
                    key={`${hit.index}:${hit.id}`}
                    onClick={() => {
                      setSelectedRecord(hit)
                    }}
                    type="button"
                  >
                    <span>{hit.id}</span>
                    <small>{hit.index}</small>
                  </button>
                ))
              )}
            </section>
          </div>
        </aside>
      )}
    </div>
  )
}
