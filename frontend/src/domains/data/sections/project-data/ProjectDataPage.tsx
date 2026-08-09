import { useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'

import {
  createProjectDataTable,
  deleteProjectDataQuery,
  fetchProjectDataRowLinks,
  materializeProjectDataTable,
  runProjectDataQuery,
  saveProjectDataQuery,
} from '../../api/project-data-studio-api'
import { projectDataStudioQueryOptions } from '../../api/project-data-studio-query'
import type {
  ProjectDataColumnDraft,
  ProjectDataRowLink,
  ProjectDataSource,
  ProjectDataTab,
} from '../../model/project-data-studio'
import { DataTabs } from '../../../../shared/components/data-tabs/DataTabs'

type SidebarPane = 'tables' | 'indexes' | 'queries'
type ResultsPane = 'table' | 'chart'

const columnTypes = [
  'text',
  'integer',
  'bigint',
  'numeric',
  'boolean',
  'timestamptz',
  'date',
  'uuid',
  'jsonb',
]

function defaultSql(name: string, source: ProjectDataSource): string {
  return source === 'opensearch'
    ? `SELECT * FROM \`${name.replaceAll('`', '``')}\` LIMIT 100;`
    : `SELECT * FROM "${name.replaceAll('"', '""')}" LIMIT 100;`
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'The request could not be completed.'
}

function isJson(value: string | null): boolean {
  if (value === null || (!value.trim().startsWith('{') && !value.trim().startsWith('[')))
    return false
  try {
    JSON.parse(value)
    return true
  } catch {
    return false
  }
}

export function ProjectDataPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const { data } = useSuspenseQuery(projectDataStudioQueryOptions(projectId))
  const queryClient = useQueryClient()
  const [pane, setPane] = useState<SidebarPane>('tables')
  const [filter, setFilter] = useState('')
  const [resultFilter, setResultFilter] = useState('')
  const [resultsPane, setResultsPane] = useState<ResultsPane>('table')
  const [tabs, setTabs] = useState<ProjectDataTab[]>([])
  const [activeKey, setActiveKey] = useState<string | null>(null)
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null)
  const [saveDialog, setSaveDialog] = useState(false)
  const [saveName, setSaveName] = useState('')
  const [createDialog, setCreateDialog] = useState(false)
  const [tableName, setTableName] = useState('')
  const [columns, setColumns] = useState<ProjectDataColumnDraft[]>([
    { name: 'id', type: 'uuid', notNull: true, primaryKey: true },
  ])
  const [materializeTable, setMaterializeTable] = useState<string | null>(null)
  const [indexName, setIndexName] = useState('')
  const [busy, setBusy] = useState(false)
  const [jsonValue, setJsonValue] = useState<{ column: string; value: string } | null>(null)
  const [rowLinks, setRowLinks] = useState<{
    title: string
    links: ProjectDataRowLink[]
  } | null>(null)

  const activeTab = tabs.find((tab) => tab.key === activeKey) ?? null
  const normalizedFilter = filter.trim().toLocaleLowerCase()
  const tables = data.tables.filter((table) =>
    table.name.toLocaleLowerCase().includes(normalizedFilter),
  )
  const indices = data.indices.filter((index) =>
    index.name.toLocaleLowerCase().includes(normalizedFilter),
  )
  const queries = data.savedQueries.filter((query) =>
    query.name.toLocaleLowerCase().includes(normalizedFilter),
  )

  const visibleRows = useMemo(() => {
    if (activeTab?.result === null || activeTab?.result === undefined) return []
    const search = resultFilter.trim().toLocaleLowerCase()
    return activeTab.result.rows
      .map((row, index) => ({ row, index }))
      .filter(
        ({ row, index }) =>
          search === '' ||
          String(index + 1).includes(search) ||
          row.some((value) => value?.toLocaleLowerCase().includes(search) ?? false),
      )
  }, [activeTab, resultFilter])

  const chart = useMemo(() => {
    if (activeTab?.result === null || activeTab?.result === undefined) return null
    const result = activeTab.result
    const numericIndex = result.columns.findIndex((_, columnIndex) =>
      result.rows.some((row) => Number.isFinite(Number(row[columnIndex]))),
    )
    if (numericIndex < 0) return null
    const points = result.rows.slice(0, 30).map((row, index) => ({
      label: row[0] ?? String(index + 1),
      value: Number(row[numericIndex] ?? 0),
    }))
    const maximum = Math.max(...points.map((point) => Math.abs(point.value)), 1)
    return { name: result.columns[numericIndex], points, maximum }
  }, [activeTab])

  function updateTab(key: string, update: Partial<ProjectDataTab>): void {
    setTabs((current) => current.map((tab) => (tab.key === key ? { ...tab, ...update } : tab)))
  }

  async function execute(tab: ProjectDataTab): Promise<void> {
    updateTab(tab.key, { running: true, error: null })
    try {
      const result = await runProjectDataQuery(
        projectId,
        tab.sql,
        tab.source,
        AbortSignal.timeout(60_000),
      )
      updateTab(tab.key, { result, running: false })
    } catch (error: unknown) {
      updateTab(tab.key, { error: errorMessage(error), result: null, running: false })
    }
  }

  function openResource(name: string, source: ProjectDataSource, sql?: string): void {
    const key = `${source}:${name}`
    const existing = tabs.find((tab) => tab.key === key)
    setActiveKey(key)
    setResultsPane('table')
    setResultFilter('')
    if (existing !== undefined) return
    const tab: ProjectDataTab = {
      key,
      name,
      source,
      sql: sql ?? defaultSql(name, source),
      result: null,
      error: null,
      running: false,
    }
    setTabs((current) => [...current, tab])
    void execute(tab)
  }

  function closeTab(key: string): void {
    setTabs((current) => {
      const index = current.findIndex((tab) => tab.key === key)
      const next = current.filter((tab) => tab.key !== key)
      if (key === activeKey) setActiveKey(next[Math.max(0, index - 1)]?.key ?? null)
      return next
    })
  }

  async function refresh(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: ['project-data-studio', projectId] })
  }

  async function saveQuery(): Promise<void> {
    if (activeTab === null || saveName.trim() === '') return
    setBusy(true)
    try {
      await saveProjectDataQuery(
        projectId,
        saveName.trim(),
        activeTab.sql,
        AbortSignal.timeout(30_000),
      )
      await refresh()
      setSaveDialog(false)
      setMessage({ text: `Saved query “${saveName.trim()}”.`, error: false })
    } catch (error: unknown) {
      setMessage({ text: errorMessage(error), error: true })
    } finally {
      setBusy(false)
    }
  }

  async function removeQuery(queryId: string): Promise<void> {
    try {
      await deleteProjectDataQuery(projectId, queryId, AbortSignal.timeout(30_000))
      await refresh()
    } catch (error: unknown) {
      setMessage({ text: errorMessage(error), error: true })
    }
  }

  async function createTable(): Promise<void> {
    const namedColumns = columns.filter((column) => column.name.trim() !== '')
    if (tableName.trim() === '' || namedColumns.length === 0) return
    setBusy(true)
    try {
      await createProjectDataTable(
        projectId,
        tableName.trim(),
        namedColumns,
        AbortSignal.timeout(30_000),
      )
      await refresh()
      setCreateDialog(false)
      setMessage({ text: `Created table “${tableName.trim()}”.`, error: false })
      setTableName('')
      setColumns([{ name: 'id', type: 'uuid', notNull: true, primaryKey: true }])
    } catch (error: unknown) {
      setMessage({ text: errorMessage(error), error: true })
    } finally {
      setBusy(false)
    }
  }

  async function materialize(): Promise<void> {
    if (materializeTable === null || indexName.trim() === '') return
    setBusy(true)
    try {
      const result = await materializeProjectDataTable(
        projectId,
        materializeTable,
        indexName.trim(),
        AbortSignal.timeout(120_000),
      )
      await refresh()
      setMaterializeTable(null)
      setMessage({
        text: `Indexed ${String(result.rowsIndexed)} rows from ${result.sourceTable} as ${result.indexName}.`,
        error: false,
      })
    } catch (error: unknown) {
      setMessage({ text: errorMessage(error), error: true })
    } finally {
      setBusy(false)
    }
  }

  async function showLinks(row: (string | null)[], rowIndex: number): Promise<void> {
    if (activeTab?.result === null || activeTab?.result === undefined) return
    const values = Object.fromEntries(
      activeTab.result.columns.map((column, index) => [column, row[index] ?? null]),
    )
    try {
      const links = await fetchProjectDataRowLinks(
        projectId,
        activeTab.name,
        values,
        AbortSignal.timeout(30_000),
      )
      setRowLinks({ title: `${activeTab.name} · row ${String(rowIndex + 1)}`, links })
    } catch (error: unknown) {
      setMessage({ text: errorMessage(error), error: true })
    }
  }

  return (
    <div className="project-data-page-react">
      <title>PlaceContext — SQL Studio</title>
      <DataTabs active="records" projectId={projectId} />
      {message === null ? null : (
        <div className={message.error ? 'status-message error' : 'status-message'} role="status">
          {message.text}
        </div>
      )}
      <section className="data-studio-react">
        <aside className="data-studio-sidebar">
          <nav aria-label="SQL Studio resources">
            {(['tables', 'indexes', 'queries'] as const).map((item) => (
              <button
                className={pane === item ? 'active' : ''}
                key={item}
                onClick={() => {
                  setPane(item)
                }}
                type="button"
              >
                {item}
              </button>
            ))}
          </nav>
          {pane === 'tables' ? (
            <button
              className="data-new-resource"
              onClick={() => {
                setCreateDialog(true)
              }}
              type="button"
            >
              ＋ New table
            </button>
          ) : null}
          <input
            aria-label={`Search ${pane}`}
            className="dcinput data-resource-search"
            onChange={(event) => {
              setFilter(event.target.value)
            }}
            placeholder={`Search ${pane}`}
            value={filter}
          />
          <div className="data-resource-list">
            {pane === 'tables'
              ? tables.map((table) => (
                  <article
                    className={activeKey === `postgres:${table.name}` ? 'active' : ''}
                    key={table.name}
                  >
                    <button
                      onClick={() => {
                        openResource(table.name, 'postgres')
                      }}
                      type="button"
                    >
                      <strong>{table.name}</strong>
                      <span>
                        {table.isView ? 'view' : table.readOnly ? 'system' : 'table'} ·{' '}
                        {table.rowEstimate} rows
                      </span>
                    </button>
                    <button
                      aria-label={`Materialize ${table.name}`}
                      onClick={() => {
                        setMaterializeTable(table.name)
                        setIndexName(`pc-${table.name.toLocaleLowerCase().replaceAll('_', '-')}`)
                      }}
                      title="Materialize to OpenSearch"
                      type="button"
                    >
                      ⇩
                    </button>
                  </article>
                ))
              : pane === 'indexes'
                ? indices.map((index) => (
                    <article
                      className={activeKey === `opensearch:${index.name}` ? 'active' : ''}
                      key={index.name}
                    >
                      <button
                        onClick={() => {
                          openResource(index.name, 'opensearch')
                        }}
                        type="button"
                      >
                        <strong>{index.name}</strong>
                        <span>
                          {index.documentCount.toLocaleString()} docs · {index.storeSize ?? '—'}
                        </span>
                      </button>
                    </article>
                  ))
                : queries.map((query) => (
                    <article
                      className={activeKey === `postgres:${query.name}` ? 'active' : ''}
                      key={query.id}
                    >
                      <button
                        onClick={() => {
                          openResource(query.name, 'postgres', query.sql)
                        }}
                        title={query.sql}
                        type="button"
                      >
                        <strong>{query.name}</strong>
                        <span>{query.sql.replaceAll(/\s+/g, ' ').slice(0, 55)}</span>
                      </button>
                      <button
                        aria-label={`Delete ${query.name}`}
                        onClick={() => {
                          void removeQuery(query.id)
                        }}
                        type="button"
                      >
                        ×
                      </button>
                    </article>
                  ))}
            {(pane === 'tables' && tables.length === 0) ||
            (pane === 'indexes' && indices.length === 0) ||
            (pane === 'queries' && queries.length === 0) ? (
              <p>No {pane} match.</p>
            ) : null}
          </div>
        </aside>

        <main className="data-studio-main">
          {tabs.length === 0 ? (
            <div className="data-studio-empty">
              <strong>SQL Studio</strong>
              <p>Select a table, index, or saved query to begin.</p>
            </div>
          ) : (
            <>
              <header>
                <div className="data-studio-tabs">
                  {tabs.map((tab) => (
                    <button
                      className={tab.key === activeKey ? 'active' : ''}
                      key={tab.key}
                      onClick={() => {
                        setActiveKey(tab.key)
                      }}
                      type="button"
                    >
                      {tab.name}
                      <span
                        onClick={(event) => {
                          event.stopPropagation()
                          closeTab(tab.key)
                        }}
                        role="button"
                        tabIndex={0}
                      >
                        ×
                      </span>
                    </button>
                  ))}
                </div>
                <div>
                  <button
                    className="dcbtn primary"
                    disabled={activeTab?.running ?? true}
                    onClick={() => {
                      if (activeTab !== null) void execute(activeTab)
                    }}
                    type="button"
                  >
                    {activeTab?.running === true ? 'Running…' : '▶ Run'}
                  </button>
                  <button
                    className="dcbtn"
                    disabled={activeTab === null}
                    onClick={() => {
                      setSaveName(activeTab?.name ?? 'untitled-query')
                      setSaveDialog(true)
                    }}
                    type="button"
                  >
                    Save
                  </button>
                </div>
              </header>
              {activeTab === null ? null : (
                <>
                  <textarea
                    aria-label="SQL query"
                    className="data-sql-editor"
                    onChange={(event) => {
                      updateTab(activeTab.key, { sql: event.target.value })
                    }}
                    spellCheck={false}
                    value={activeTab.sql}
                  />
                  <div className="data-results-bar">
                    <input
                      aria-label="Search results"
                      className="dcinput"
                      onChange={(event) => {
                        setResultFilter(event.target.value)
                      }}
                      placeholder="Search results…"
                      value={resultFilter}
                    />
                    <span className={activeTab.error === null ? '' : 'error'}>
                      {activeTab.error ??
                        (activeTab.result === null
                          ? 'Run a query to see results'
                          : activeTab.result.columns.length === 0
                            ? `OK — ${String(activeTab.result.affectedRows)} row(s) affected.`
                            : `${String(visibleRows.length)} of ${String(activeTab.result.rows.length)} row(s)${activeTab.result.truncated ? ' · truncated' : ''}`)}
                    </span>
                    <div>
                      <button
                        className={resultsPane === 'table' ? 'active' : ''}
                        onClick={() => {
                          setResultsPane('table')
                        }}
                        type="button"
                      >
                        Table
                      </button>
                      <button
                        className={resultsPane === 'chart' ? 'active' : ''}
                        onClick={() => {
                          setResultsPane('chart')
                        }}
                        type="button"
                      >
                        Chart
                      </button>
                    </div>
                  </div>
                  {resultsPane === 'chart' ? (
                    <div className="data-result-chart">
                      {chart === null ? (
                        <p>Run a query with a numeric column to see a chart.</p>
                      ) : (
                        <>
                          <strong>{chart.name}</strong>
                          {chart.points.map((point, index) => (
                            <div key={`${point.label}:${String(index)}`}>
                              <span>{point.label}</span>
                              <i
                                style={{
                                  width: `${String((Math.abs(point.value) / chart.maximum) * 100)}%`,
                                }}
                              />
                              <em>{point.value.toLocaleString()}</em>
                            </div>
                          ))}
                        </>
                      )}
                    </div>
                  ) : activeTab.result === null || activeTab.result.columns.length === 0 ? null : (
                    <div className="data-result-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>#</th>
                            {activeTab.result.columns.map((column) => (
                              <th key={column}>{column}</th>
                            ))}
                            {activeTab.source === 'postgres' ? <th>Links</th> : null}
                          </tr>
                        </thead>
                        <tbody>
                          {visibleRows.map(({ row, index }) => (
                            <tr key={String(index)}>
                              <td>{index + 1}</td>
                              {activeTab.result?.columns.map((column, columnIndex) => {
                                const value = row[columnIndex] ?? null
                                return (
                                  <td key={column}>
                                    {isJson(value) ? (
                                      <button
                                        className="dclink"
                                        onClick={() => {
                                          setJsonValue({ column, value: value ?? '' })
                                        }}
                                        type="button"
                                      >
                                        View data
                                      </button>
                                    ) : (
                                      (value ?? '∅')
                                    )}
                                  </td>
                                )
                              })}
                              {activeTab.source === 'postgres' ? (
                                <td>
                                  <button
                                    aria-label={`Show links for row ${String(index + 1)}`}
                                    className="dclink"
                                    onClick={() => {
                                      void showLinks(row, index)
                                    }}
                                    type="button"
                                  >
                                    ⤢
                                  </button>
                                </td>
                              ) : null}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </main>
      </section>

      {saveDialog ? (
        <Dialog
          title="Save query"
          onClose={() => {
            setSaveDialog(false)
          }}
        >
          <label>
            Name
            <input
              className="dcinput"
              onChange={(event) => {
                setSaveName(event.target.value)
              }}
              value={saveName}
            />
          </label>
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() => {
              void saveQuery()
            }}
            type="button"
          >
            {busy ? 'Saving…' : 'Save'}
          </button>
        </Dialog>
      ) : null}
      {createDialog ? (
        <Dialog
          title="New table"
          onClose={() => {
            setCreateDialog(false)
          }}
        >
          <label>
            Table name
            <input
              className="dcinput"
              onChange={(event) => {
                setTableName(event.target.value)
              }}
              value={tableName}
            />
          </label>
          <div className="data-column-list">
            {columns.map((column, index) => (
              <div key={String(index)}>
                <input
                  aria-label={`Column ${String(index + 1)} name`}
                  className="dcinput"
                  onChange={(event) => {
                    setColumns((current) =>
                      current.map((item, itemIndex) =>
                        itemIndex === index ? { ...item, name: event.target.value } : item,
                      ),
                    )
                  }}
                  value={column.name}
                />
                <select
                  aria-label={`Column ${String(index + 1)} type`}
                  className="dcinput"
                  onChange={(event) => {
                    setColumns((current) =>
                      current.map((item, itemIndex) =>
                        itemIndex === index ? { ...item, type: event.target.value } : item,
                      ),
                    )
                  }}
                  value={column.type}
                >
                  {columnTypes.map((type) => (
                    <option key={type}>{type}</option>
                  ))}
                </select>
                <label>
                  <input
                    checked={column.notNull}
                    onChange={(event) => {
                      setColumns((current) =>
                        current.map((item, itemIndex) =>
                          itemIndex === index ? { ...item, notNull: event.target.checked } : item,
                        ),
                      )
                    }}
                    type="checkbox"
                  />
                  not null
                </label>
                <label>
                  <input
                    checked={column.primaryKey}
                    onChange={(event) => {
                      setColumns((current) =>
                        current.map((item, itemIndex) =>
                          itemIndex === index
                            ? { ...item, primaryKey: event.target.checked }
                            : item,
                        ),
                      )
                    }}
                    type="checkbox"
                  />
                  PK
                </label>
                <button
                  aria-label={`Remove column ${String(index + 1)}`}
                  onClick={() => {
                    setColumns((current) => current.filter((_, itemIndex) => itemIndex !== index))
                  }}
                  type="button"
                >
                  ×
                </button>
              </div>
            ))}
          </div>
          <button
            className="dcbtn"
            onClick={() => {
              setColumns((current) => [
                ...current,
                { name: '', type: 'text', notNull: false, primaryKey: false },
              ])
            }}
            type="button"
          >
            ＋ Column
          </button>
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() => {
              void createTable()
            }}
            type="button"
          >
            {busy ? 'Creating…' : 'Create table'}
          </button>
        </Dialog>
      ) : null}
      {materializeTable === null ? null : (
        <Dialog
          title="Materialize to index"
          onClose={() => {
            setMaterializeTable(null)
          }}
        >
          <p>
            Copy <strong>{materializeTable}</strong> into an OpenSearch index.
          </p>
          <label>
            Index name
            <input
              className="dcinput"
              onChange={(event) => {
                setIndexName(event.target.value)
              }}
              value={indexName}
            />
          </label>
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() => {
              void materialize()
            }}
            type="button"
          >
            {busy ? 'Materializing…' : 'Materialize'}
          </button>
        </Dialog>
      )}
      {jsonValue === null ? null : (
        <aside className="data-side-pane">
          <header>
            <div>
              <strong>{jsonValue.column}</strong>
              <span>JSON data</span>
            </div>
            <button
              aria-label="Close JSON viewer"
              onClick={() => {
                setJsonValue(null)
              }}
              type="button"
            >
              ×
            </button>
          </header>
          <pre>{JSON.stringify(JSON.parse(jsonValue.value) as unknown, null, 2)}</pre>
        </aside>
      )}
      {rowLinks === null ? null : (
        <aside className="data-side-pane">
          <header>
            <div>
              <strong>{rowLinks.title}</strong>
              <span>{rowLinks.links.length} linked records</span>
            </div>
            <button
              aria-label="Close linked records"
              onClick={() => {
                setRowLinks(null)
              }}
              type="button"
            >
              ×
            </button>
          </header>
          <div className="data-link-list">
            {rowLinks.links.length === 0 ? (
              <p>No linked records found.</p>
            ) : (
              rowLinks.links.map((link) => (
                <article
                  className="dccard"
                  key={`${link.tableName}:${link.columnName}:${link.rowKey}`}
                >
                  <small>{link.kind}</small>
                  <strong>{link.displayValue}</strong>
                  <span>
                    {link.tableName} · {link.columnName} · {link.rowKey}
                  </span>
                </article>
              ))
            )}
          </div>
        </aside>
      )}
    </div>
  )
}

function Dialog({
  children,
  onClose,
  title,
}: {
  children: ReactNode
  onClose: () => void
  title: string
}) {
  return (
    <div className="data-dialog-backdrop">
      <section className="data-dialog dccard" role="dialog" aria-modal="true" aria-label={title}>
        <header>
          <strong>{title}</strong>
          <button aria-label="Close" onClick={onClose} type="button">
            ×
          </button>
        </header>
        <div>{children}</div>
      </section>
    </div>
  )
}
