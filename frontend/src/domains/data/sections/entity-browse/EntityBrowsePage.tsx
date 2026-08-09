import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useDeferredValue, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import {
  createEntityRecord,
  deleteEntityRecord,
  fetchEntityRecordLinks,
  updateEntityRecord,
} from '../../api/entity-browse-api'
import { entityBrowseQueryOptions } from '../../api/entity-browse-query'
import type { EntityBrowseModel, RecordLink } from '../../model/entity-browse'

type RecordValues = Record<string, string | null>

function rowValues(model: EntityBrowseModel, row: (string | null)[]): RecordValues {
  return Object.fromEntries(model.page.columns.map((column, index) => [column, row[index] ?? null]))
}

function rowKeys(model: EntityBrowseModel, values: RecordValues): RecordValues {
  const primaryKeys = model.columns.filter((column) => column.primaryKey)
  if (primaryKeys.length > 0) {
    return Object.fromEntries(
      primaryKeys.map((column) => [column.name, values[column.name] ?? null]),
    )
  }
  const labelColumn = model.entity.labelColumn
  if (labelColumn !== null && values[labelColumn] !== undefined) {
    return { [labelColumn]: values[labelColumn] ?? null }
  }
  return Object.fromEntries(
    Object.entries(values)
      .filter(([, value]) => value !== null && value !== '')
      .slice(0, 3),
  )
}

function inputType(databaseType: string): string {
  if (/int|numeric|double|real|decimal/i.test(databaseType)) return 'number'
  if (/timestamp/i.test(databaseType)) return 'datetime-local'
  if (/^date$/i.test(databaseType)) return 'date'
  return 'text'
}

export function EntityBrowsePage() {
  const { projectId = '', entityName = '' } = useParams<{
    projectId: string
    entityName: string
  }>()
  const decodedEntityName = decodeURIComponent(entityName)
  const [searchInput, setSearchInput] = useState('')
  const search = useDeferredValue(searchInput)
  const [pageNumber, setPageNumber] = useState(1)
  const options = entityBrowseQueryOptions(projectId, decodedEntityName, search, pageNumber)
  const { data } = useSuspenseQuery(options)
  const queryClient = useQueryClient()
  const [openValues, setOpenValues] = useState<RecordValues | null>(null)
  const [editValues, setEditValues] = useState<RecordValues | null>(null)
  const [creating, setCreating] = useState(false)
  const [links, setLinks] = useState<RecordLink[]>([])
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null)

  const visibleColumns = useMemo(
    () => data.columns.filter((column) => data.page.columns.includes(column.name)),
    [data],
  )
  const writeMutation = useMutation({
    mutationFn: async (command: 'create' | 'update' | 'delete') => {
      const signal = AbortSignal.timeout(30_000)
      if (command === 'create' && editValues !== null) {
        return createEntityRecord(projectId, decodedEntityName, editValues, signal)
      }
      if (command === 'update' && editValues !== null && openValues !== null) {
        return updateEntityRecord(
          projectId,
          decodedEntityName,
          rowKeys(data, openValues),
          editValues,
          signal,
        )
      }
      if (command === 'delete' && openValues !== null) {
        return deleteEntityRecord(projectId, decodedEntityName, rowKeys(data, openValues), signal)
      }
      throw new Error('No record is selected.')
    },
  })

  async function refresh(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: ['entity-browse', projectId, entityName] })
  }

  async function openRecord(row: (string | null)[]): Promise<void> {
    const values = rowValues(data, row)
    setOpenValues(values)
    setEditValues(null)
    setCreating(false)
    setLinks([])
    try {
      setLinks(
        await fetchEntityRecordLinks(
          projectId,
          decodedEntityName,
          values,
          AbortSignal.timeout(30_000),
        ),
      )
    } catch {
      setLinks([])
    }
  }

  function startCreate(): void {
    setCreating(true)
    setOpenValues(null)
    setEditValues(Object.fromEntries(data.columns.map((column) => [column.name, null])))
  }

  function startEdit(): void {
    if (openValues === null) return
    setCreating(false)
    setEditValues({ ...openValues })
  }

  async function saveRecord(): Promise<void> {
    try {
      const result = await writeMutation.mutateAsync(creating ? 'create' : 'update')
      await refresh()
      setEditValues(null)
      setOpenValues(null)
      if ('duplicateWarnings' in result && result.duplicateWarnings.length > 0) {
        setMessage({
          text: `Created with possible duplicate(s): ${result.duplicateWarnings.join('; ')}`,
          error: false,
        })
      } else {
        setMessage({ text: creating ? 'Record created.' : 'Record updated.', error: false })
      }
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The record could not be saved.',
        error: true,
      })
    }
  }

  async function removeRecord(): Promise<void> {
    try {
      await writeMutation.mutateAsync('delete')
      await refresh()
      setOpenValues(null)
      setMessage({ text: 'Record deleted.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The record could not be deleted.',
        error: true,
      })
    }
  }

  const totalPages = Math.max(1, Math.ceil(data.page.totalCount / data.page.pageSize))
  const labelColumn = data.entity.labelColumn ?? data.page.columns[0]

  return (
    <div className="entity-browse-page-react">
      <title>PlaceContext — {data.entity.name}</title>
      <header className="entity-browse-head-react">
        <div>
          <h1>{data.entity.name}</h1>
          <p>
            <code>{data.entity.tableName}</code> · {data.page.totalCount} records
            {data.entity.relations.length === 0
              ? ''
              : ` · related to ${data.entity.relations
                  .map((relation) => relation.targetEntity)
                  .join(', ')}`}
          </p>
        </div>
        <div>
          <button className="dcbtn primary" onClick={startCreate} type="button">
            ＋ New
          </button>
          <input
            className="dcinput"
            onChange={(event) => {
              setSearchInput(event.target.value)
              setPageNumber(1)
            }}
            placeholder="search records…"
            value={searchInput}
          />
        </div>
      </header>

      {message === null ? null : (
        <div className={message.error ? 'status-message error' : 'status-message'} role="status">
          {message.text}
        </div>
      )}

      <nav aria-label="Entity views" className="dctabs">
        <button className="dctab active" type="button">
          Records
        </button>
        <Link className="dctab" to={`/project/${projectId}/data-graph`}>
          Graph
        </Link>
        <Link className="dctab" to={`/project/${projectId}/analytics`}>
          Analytics
        </Link>
      </nav>

      <section className="dccard entity-record-table-react">
        <div className="entity-record-scroll-react">
          <table>
            <thead>
              <tr>
                {data.page.columns.map((column) => (
                  <th key={column}>{column}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.page.rows.length === 0 ? (
                <tr>
                  <td colSpan={Math.max(1, data.page.columns.length)}>
                    {search === '' ? 'No records yet.' : 'No records match your search.'}
                  </td>
                </tr>
              ) : (
                data.page.rows.map((row, rowIndex) => (
                  <tr
                    key={`${String(rowIndex)}:${row.join(':')}`}
                    onClick={() => void openRecord(row)}
                  >
                    {data.page.columns.map((column, columnIndex) => (
                      <td className={column === labelColumn ? 'label-cell-react' : ''} key={column}>
                        {row[columnIndex] ?? '—'}
                      </td>
                    ))}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        <footer>
          <span>
            {data.page.totalCount === 0
              ? '0 records'
              : `Showing ${String((data.page.page - 1) * data.page.pageSize + 1)}–${String(
                  Math.min(data.page.page * data.page.pageSize, data.page.totalCount),
                )} of ${String(data.page.totalCount)}`}
          </span>
          <div>
            <button
              className="dcbtn"
              disabled={pageNumber <= 1}
              onClick={() => {
                setPageNumber((value) => Math.max(1, value - 1))
              }}
              type="button"
            >
              ‹ Prev
            </button>
            <span>
              Page {pageNumber} of {totalPages}
            </span>
            <button
              className="dcbtn"
              disabled={pageNumber >= totalPages}
              onClick={() => {
                setPageNumber((value) => Math.min(totalPages, value + 1))
              }}
              type="button"
            >
              Next ›
            </button>
          </div>
        </footer>
      </section>

      {openValues === null && editValues === null ? null : (
        <div className="entity-record-panel-react">
          <button
            aria-label="Close record"
            className="entity-record-scrim-react"
            onClick={() => {
              setOpenValues(null)
              setEditValues(null)
            }}
            type="button"
          />
          <section>
            <header>
              <div>
                <strong>
                  {creating
                    ? `New ${data.entity.name}`
                    : editValues === null
                      ? (openValues?.[labelColumn ?? ''] ?? data.entity.name)
                      : 'Edit record'}
                </strong>
                <span>{data.entity.name}</span>
              </div>
              <div>
                {editValues !== null ? null : (
                  <>
                    <button className="dcbtn" onClick={startEdit} type="button">
                      Edit
                    </button>
                    <button
                      className="dcbtn danger"
                      onClick={() => void removeRecord()}
                      type="button"
                    >
                      Delete
                    </button>
                  </>
                )}
                <button
                  aria-label="Close"
                  onClick={() => {
                    setOpenValues(null)
                    setEditValues(null)
                  }}
                  type="button"
                >
                  ×
                </button>
              </div>
            </header>
            <div className="entity-record-body-react">
              {editValues === null && openValues !== null ? (
                <>
                  <dl>
                    {data.page.columns.map((column) => (
                      <div key={column}>
                        <dt>{column}</dt>
                        <dd>{openValues[column] ?? '—'}</dd>
                      </div>
                    ))}
                  </dl>
                  {links.length === 0 ? null : (
                    <section className="entity-linked-records-react">
                      <h2>Auto-linked records</h2>
                      {links.map((link) => (
                        <Link
                          className="dccard"
                          key={`${link.tableName}:${link.columnName}:${link.rowKey}`}
                          to={`/project/${projectId}/entity/${encodeURIComponent(link.tableName)}?record=${encodeURIComponent(link.rowKey)}`}
                        >
                          <small>{link.kind}</small>
                          <strong>{link.displayValue}</strong>
                          <span>
                            {link.tableName} · {link.columnName} · {link.rowKey}
                          </span>
                        </Link>
                      ))}
                    </section>
                  )}
                </>
              ) : (
                <form
                  className="entity-record-form-react"
                  onSubmit={(event) => {
                    event.preventDefault()
                    void saveRecord()
                  }}
                >
                  {visibleColumns.map((column) => (
                    <label key={column.name}>
                      {column.name}
                      {column.type === 'boolean' ? (
                        <input
                          checked={editValues?.[column.name] === 'true'}
                          onChange={(event) => {
                            setEditValues({
                              ...editValues,
                              [column.name]: event.target.checked ? 'true' : 'false',
                            })
                          }}
                          type="checkbox"
                        />
                      ) : (
                        <input
                          className="dcinput"
                          onChange={(event) => {
                            setEditValues({
                              ...editValues,
                              [column.name]: event.target.value || null,
                            })
                          }}
                          required={column.notNull}
                          type={inputType(column.type)}
                          value={editValues?.[column.name] ?? ''}
                        />
                      )}
                    </label>
                  ))}
                  <footer>
                    <button
                      className="dcbtn"
                      onClick={() => {
                        setEditValues(null)
                      }}
                      type="button"
                    >
                      Cancel
                    </button>
                    <button
                      className="dcbtn primary"
                      disabled={writeMutation.isPending}
                      type="submit"
                    >
                      {writeMutation.isPending ? 'Saving…' : creating ? 'Create' : 'Save'}
                    </button>
                  </footer>
                </form>
              )}
            </div>
          </section>
        </div>
      )}
    </div>
  )
}
