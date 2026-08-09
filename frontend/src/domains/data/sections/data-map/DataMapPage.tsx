import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'

import { DataTabs } from '../../../../shared/components/data-tabs/DataTabs'
import { deleteDataMapping, saveDataMapping } from '../../api/data-admin-api'
import { dataAdminQueryOptions } from '../../api/data-admin-query'
import type { DataMapping, SaveDataMappingRequest } from '../../model/data-admin'

const columnTypes = [
  'text',
  'integer',
  'bigint',
  'numeric',
  'boolean',
  'date',
  'timestamp',
  'jsonb',
]

function emptyMapping(jobId = '', targetTable = ''): SaveDataMappingRequest {
  return {
    id: null,
    jobId,
    targetTable,
    rowsPath: null,
    fields: [{ sourcePath: '', column: '', type: 'text' }],
    enabled: true,
  }
}

function editMapping(mapping: DataMapping): SaveDataMappingRequest {
  return {
    id: mapping.id,
    jobId: mapping.jobId,
    targetTable: mapping.targetTable,
    rowsPath: mapping.rowsPath,
    fields: mapping.fields.map((field) => ({ ...field })),
    enabled: mapping.enabled,
  }
}

export function DataMapPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const query = dataAdminQueryOptions(projectId)
  const { data } = useSuspenseQuery(query)
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState<SaveDataMappingRequest | null>(null)
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null)
  const saveMutation = useMutation({
    mutationFn: (value: SaveDataMappingRequest) =>
      saveDataMapping(projectId, value, AbortSignal.timeout(30_000)),
  })
  const deleteMutation = useMutation({
    mutationFn: (mappingId: string) =>
      deleteDataMapping(projectId, mappingId, AbortSignal.timeout(30_000)),
  })

  const mappedJobs = new Set(data.mappings.map((mapping) => mapping.jobId))
  const orderedJobs = [...data.jobs].sort((left, right) => {
    const mappedDifference = Number(mappedJobs.has(left.id)) - Number(mappedJobs.has(right.id))
    return mappedDifference === 0 ? left.name.localeCompare(right.name) : mappedDifference
  })

  async function refresh(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: query.queryKey })
  }

  async function save(): Promise<void> {
    if (draft === null) return
    const fields = draft.fields.filter(
      (field) => field.sourcePath.trim() !== '' || field.column.trim() !== '',
    )
    if (draft.jobId === '' || draft.targetTable.trim() === '' || fields.length === 0) {
      setMessage({ text: 'Choose a job and table, then define at least one field.', error: true })
      return
    }
    try {
      await saveMutation.mutateAsync({ ...draft, fields })
      await refresh()
      setDraft(null)
      setMessage({ text: 'Data mapping saved.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The mapping could not be saved.',
        error: true,
      })
    }
  }

  async function remove(): Promise<void> {
    if (draft?.id === null || draft?.id === undefined) return
    try {
      await deleteMutation.mutateAsync(draft.id)
      await refresh()
      setDraft(null)
      setMessage({ text: 'Data mapping deleted.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The mapping could not be deleted.',
        error: true,
      })
    }
  }

  return (
    <div className="data-admin-page-react">
      <title>PlaceContext — Data map</title>
      <DataTabs active="data-map" projectId={projectId} />
      <header className="data-admin-head-react">
        <div>
          <h1>Data map</h1>
          <p>Choose which Job results become queryable project tables after a completed run.</p>
        </div>
        <button
          className="dcbtn primary"
          onClick={() => {
            setDraft(emptyMapping())
          }}
          type="button"
        >
          ＋ New mapping
        </button>
      </header>

      {message === null ? null : (
        <div className={message.error ? 'status-message error' : 'status-message'} role="status">
          {message.text}
        </div>
      )}

      <section className="summary-strip" aria-label="Data mapping summary">
        <div>
          <strong>{data.jobs.length}</strong>
          <span>jobs</span>
        </div>
        <div>
          <strong>{mappedJobs.size}</strong>
          <span>mapped jobs</span>
        </div>
        <div>
          <strong>{data.mappings.filter((mapping) => mapping.enabled).length}</strong>
          <span>active mappings</span>
        </div>
        <div>
          <strong>{Math.max(0, data.jobs.length - mappedJobs.size)}</strong>
          <span>need mapping</span>
        </div>
      </section>

      <section className="dccard data-admin-suite-react">
        <header>
          <div>
            <strong>Job outputs</strong>
            <span>Mappings run after every successful or partial Job run</span>
          </div>
          <span>{data.tables.length} project tables</span>
        </header>
        {orderedJobs.length === 0 ? (
          <p className="data-admin-empty-react">No Jobs exist in this project yet.</p>
        ) : (
          orderedJobs.map((job) => {
            const mappings = data.mappings.filter((mapping) => mapping.jobId === job.id)
            return (
              <article className="data-mapping-row-react" key={job.id}>
                <span
                  className={mappings.length === 0 ? 'mapping-missing-react' : 'mapping-ok-react'}
                >
                  {mappings.length === 0 ? '!' : '✓'}
                </span>
                <div>
                  <div className="data-admin-title-row-react">
                    <strong>{job.name}</strong>
                    <small>{job.returnType} output</small>
                  </div>
                  {mappings.length === 0 ? (
                    <p>Completed runs do not populate a project table.</p>
                  ) : (
                    <div className="mapping-list-react">
                      {mappings.map((mapping) => (
                        <button
                          className="mapping-detail-react"
                          key={mapping.id}
                          onClick={() => {
                            setDraft(editMapping(mapping))
                          }}
                          type="button"
                        >
                          <strong>{mapping.targetTable}</strong>
                          <span>
                            {mapping.rowsPath === null
                              ? 'root object'
                              : `rows at ${mapping.rowsPath}`}
                            {' · '}
                            {mapping.fields.length} fields ·{' '}
                            {mapping.enabled ? 'enabled' : 'paused'}
                          </span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
                <button
                  className="dcbtn"
                  onClick={() => {
                    setDraft(
                      emptyMapping(job.id, job.name.toLowerCase().replaceAll(/[^a-z0-9]+/g, '_')),
                    )
                  }}
                  type="button"
                >
                  {mappings.length === 0 ? 'Create mapping' : '＋ Add'}
                </button>
              </article>
            )
          })
        )}
      </section>

      {draft === null ? null : (
        <div className="data-admin-modal-react">
          <section className="dccard" role="dialog" aria-label="Data mapping editor">
            <header>
              <div>
                <strong>{draft.id === null ? 'New mapping' : 'Edit mapping'}</strong>
                <span>Job result → project table</span>
              </div>
              <button
                aria-label="Close"
                onClick={() => {
                  setDraft(null)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            <div className="data-admin-form-grid-react">
              <label>
                Source Job
                <select
                  className="dcinput"
                  onChange={(event) => {
                    setDraft({ ...draft, jobId: event.target.value })
                  }}
                  value={draft.jobId}
                >
                  <option value="">— pick a Job —</option>
                  {data.jobs.map((job) => (
                    <option key={job.id} value={job.id}>
                      {job.name} ({job.returnType})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Target table
                <input
                  className="dcinput"
                  list="project-data-tables"
                  onChange={(event) => {
                    setDraft({ ...draft, targetTable: event.target.value })
                  }}
                  value={draft.targetTable}
                />
              </label>
              <datalist id="project-data-tables">
                {data.tables.map((table) => (
                  <option key={table.name} value={table.name} />
                ))}
              </datalist>
              <label>
                Records at · optional dot path
                <input
                  className="dcinput"
                  onChange={(event) => {
                    setDraft({ ...draft, rowsPath: event.target.value || null })
                  }}
                  placeholder="rows"
                  value={draft.rowsPath ?? ''}
                />
              </label>
              <label className="data-admin-check-react">
                <input
                  checked={draft.enabled}
                  onChange={(event) => {
                    setDraft({ ...draft, enabled: event.target.checked })
                  }}
                  type="checkbox"
                />
                Enabled after every completed run
              </label>
            </div>
            <div className="mapping-fields-react">
              <header>
                <strong>Fields</strong>
                <button
                  className="dcbtn"
                  onClick={() => {
                    setDraft({
                      ...draft,
                      fields: [...draft.fields, { sourcePath: '', column: '', type: 'text' }],
                    })
                  }}
                  type="button"
                >
                  ＋ Field
                </button>
              </header>
              {draft.fields.map((field, index) => (
                <div className="mapping-field-react" key={index}>
                  <input
                    aria-label={`Field ${String(index + 1)} source path`}
                    className="dcinput"
                    onChange={(event) => {
                      const fields = draft.fields.map((item, fieldIndex) =>
                        fieldIndex === index ? { ...item, sourcePath: event.target.value } : item,
                      )
                      setDraft({ ...draft, fields })
                    }}
                    placeholder="source path"
                    value={field.sourcePath}
                  />
                  <span>→</span>
                  <input
                    aria-label={`Field ${String(index + 1)} column`}
                    className="dcinput"
                    onChange={(event) => {
                      const fields = draft.fields.map((item, fieldIndex) =>
                        fieldIndex === index ? { ...item, column: event.target.value } : item,
                      )
                      setDraft({ ...draft, fields })
                    }}
                    placeholder="column"
                    value={field.column}
                  />
                  <select
                    aria-label={`Field ${String(index + 1)} type`}
                    className="dcinput"
                    onChange={(event) => {
                      const fields = draft.fields.map((item, fieldIndex) =>
                        fieldIndex === index ? { ...item, type: event.target.value } : item,
                      )
                      setDraft({ ...draft, fields })
                    }}
                    value={field.type}
                  >
                    {columnTypes.map((type) => (
                      <option key={type}>{type}</option>
                    ))}
                  </select>
                  <button
                    aria-label={`Remove field ${String(index + 1)}`}
                    className="dcbtn"
                    onClick={() => {
                      setDraft({
                        ...draft,
                        fields: draft.fields.filter((_, fieldIndex) => fieldIndex !== index),
                      })
                    }}
                    type="button"
                  >
                    ×
                  </button>
                </div>
              ))}
            </div>
            <footer>
              {draft.id === null ? null : (
                <button className="dcbtn danger" onClick={() => void remove()} type="button">
                  Delete
                </button>
              )}
              <span />
              <button
                className="dcbtn"
                onClick={() => {
                  setDraft(null)
                }}
                type="button"
              >
                Cancel
              </button>
              <button
                className="dcbtn primary"
                disabled={saveMutation.isPending}
                onClick={() => void save()}
                type="button"
              >
                Save mapping
              </button>
            </footer>
          </section>
        </div>
      )}
    </div>
  )
}
