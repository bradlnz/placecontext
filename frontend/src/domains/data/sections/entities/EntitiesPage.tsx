import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'

import { DataTabs } from '../../../../shared/components/data-tabs/DataTabs'
import { deleteDataEntity, rescanRecordLinks, saveDataEntity } from '../../api/data-admin-api'
import { dataAdminQueryOptions } from '../../api/data-admin-query'
import type { DataEntity, SaveDataEntityRequest } from '../../model/data-admin'

function emptyEntity(): SaveDataEntityRequest {
  return {
    id: null,
    name: '',
    tableName: '',
    labelColumn: null,
    relations: [],
    tags: [],
  }
}

function editEntity(entity: DataEntity): SaveDataEntityRequest {
  return {
    id: entity.id,
    name: entity.name,
    tableName: entity.tableName,
    labelColumn: entity.labelColumn,
    relations: entity.relations.map((relation) => ({ ...relation })),
    tags: [...entity.tags],
  }
}

export function EntitiesPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const query = dataAdminQueryOptions(projectId)
  const { data } = useSuspenseQuery(query)
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState<SaveDataEntityRequest | null>(null)
  const [tagsInput, setTagsInput] = useState('')
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null)
  const saveMutation = useMutation({
    mutationFn: (value: SaveDataEntityRequest) =>
      saveDataEntity(projectId, value, AbortSignal.timeout(30_000)),
  })
  const deleteMutation = useMutation({
    mutationFn: (entityId: string) =>
      deleteDataEntity(projectId, entityId, AbortSignal.timeout(30_000)),
  })
  const rescanMutation = useMutation({
    mutationFn: () => rescanRecordLinks(projectId, AbortSignal.timeout(60_000)),
  })

  function open(value: SaveDataEntityRequest): void {
    setDraft(value)
    setTagsInput(value.tags.join(', '))
  }

  async function refresh(): Promise<void> {
    await queryClient.invalidateQueries({ queryKey: query.queryKey })
  }

  async function save(): Promise<void> {
    if (draft === null) return
    const tags = tagsInput
      .split(',')
      .map((tag) => tag.trim())
      .filter((tag, index, all) => tag !== '' && all.indexOf(tag) === index)
    if (draft.name.trim() === '' || draft.tableName.trim() === '') {
      setMessage({ text: 'An entity name and source table are required.', error: true })
      return
    }
    try {
      await saveMutation.mutateAsync({ ...draft, tags })
      await refresh()
      setDraft(null)
      setMessage({ text: 'Entity saved.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The entity could not be saved.',
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
      setMessage({ text: 'Entity deleted.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The entity could not be deleted.',
        error: true,
      })
    }
  }

  async function rescan(): Promise<void> {
    try {
      const result = await rescanMutation.mutateAsync()
      await refresh()
      setMessage({
        text: `Scanned ${String(result.tablesScanned)} table(s) · ${String(result.linksFound)} link(s).`,
        error: false,
      })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'Linked values could not be rescanned.',
        error: true,
      })
    }
  }

  return (
    <div className="data-admin-page-react">
      <title>PlaceContext — Entities</title>
      <DataTabs active="entities" projectId={projectId} />
      <header className="data-admin-head-react">
        <div>
          <h1>Entities</h1>
          <p>Turn ingested tables into named business views and relate them to one another.</p>
        </div>
        <div className="data-admin-actions-react">
          <button
            className="dcbtn"
            disabled={rescanMutation.isPending}
            onClick={() => void rescan()}
            type="button"
          >
            {rescanMutation.isPending ? 'Scanning…' : '↻ Rescan links'}
          </button>
          <button className="dcbtn primary" onClick={() => { open(emptyEntity()); }} type="button">
            ＋ New entity
          </button>
        </div>
      </header>

      {message === null ? null : (
        <div className={message.error ? 'status-message error' : 'status-message'} role="status">
          {message.text}
        </div>
      )}

      <section className="summary-strip" aria-label="Entity summary">
        <div>
          <strong>{data.entities.length}</strong>
          <span>entities</span>
        </div>
        <div>
          <strong>{data.entities.reduce((total, entity) => total + entity.relations.length, 0)}</strong>
          <span>relations</span>
        </div>
        <div>
          <strong>{data.entities.reduce((total, entity) => total + entity.tags.length, 0)}</strong>
          <span>tags</span>
        </div>
      </section>

      <section className="dccard data-admin-suite-react">
        <header>
          <div>
            <strong>Entity catalogue</strong>
            <span>Business views over project data tables</span>
          </div>
        </header>
        {data.entities.length === 0 ? (
          <p className="data-admin-empty-react">
            No entities yet. Tag your first ingested table to create a business view.
          </p>
        ) : (
          data.entities.map((entity) => (
            <article className="data-entity-row-react" key={entity.id}>
              <button
                className="entity-open-react"
                onClick={() =>
                  void navigate(`/project/${projectId}/entity/${encodeURIComponent(entity.name)}`)
                }
                type="button"
              >
                ◫
              </button>
              <div>
                <div className="data-admin-title-row-react">
                  <strong>{entity.name}</strong>
                  <small>{entity.tableName}</small>
                </div>
                <p>
                  {entity.relations.length} relations · {entity.tags.length} tags
                </p>
                <div className="entity-tags-react">
                  {entity.tags.map((tag) => (
                    <span key={tag}>{tag}</span>
                  ))}
                </div>
              </div>
              <div className="data-admin-actions-react">
                <button className="dcbtn" onClick={() => { open(editEntity(entity)); }} type="button">
                  Edit
                </button>
                <button
                  className="dcbtn"
                  onClick={() =>
                    void navigate(`/project/${projectId}/entity/${encodeURIComponent(entity.name)}`)
                  }
                  type="button"
                >
                  Open records
                </button>
              </div>
            </article>
          ))
        )}
      </section>

      <section className="data-link-section-react">
        <h2>Linked values</h2>
        {data.linkGroups.length === 0 ? (
          <div className="dccard data-admin-empty-react">No linked values yet.</div>
        ) : (
          <div className="data-link-grid-react">
            {data.linkGroups.map((group) => (
              <article className="dccard" key={`${group.kind}:${group.normalizedValue}`}>
                <header>
                  <small>{group.kind}</small>
                  <strong>{group.displayValue}</strong>
                  <span>{group.occurrences.length} occurrence(s)</span>
                </header>
                {group.occurrences.slice(0, 6).map((occurrence) => (
                  <p key={`${occurrence.tableName}:${occurrence.columnName}:${occurrence.rowKey}`}>
                    {occurrence.tableName} · {occurrence.columnName} · {occurrence.rowKey}
                  </p>
                ))}
              </article>
            ))}
          </div>
        )}
      </section>

      {draft === null ? null : (
        <div className="data-admin-modal-react">
          <section className="dccard" role="dialog" aria-label="Entity editor">
            <header>
              <div>
                <strong>{draft.id === null ? 'New entity' : 'Edit entity'}</strong>
                <span>A named business view over an ingested table.</span>
              </div>
              <button aria-label="Close" onClick={() => { setDraft(null); }} type="button">
                ×
              </button>
            </header>
            <div className="data-admin-form-grid-react">
              <label>
                Name
                <input
                  className="dcinput"
                  onChange={(event) => { setDraft({ ...draft, name: event.target.value }); }}
                  placeholder="Sites"
                  value={draft.name}
                />
              </label>
              <label>
                Source table or view
                <select
                  className="dcinput"
                  onChange={(event) => { setDraft({ ...draft, tableName: event.target.value }); }}
                  value={draft.tableName}
                >
                  <option value="">— pick —</option>
                  {data.tables.map((table) => (
                    <option key={table.name} value={table.name}>
                      {table.name}
                      {table.isView ? ' (view)' : ''}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Label column
                <input
                  className="dcinput"
                  onChange={(event) => { setDraft({ ...draft, labelColumn: event.target.value || null }); }}
                  placeholder="name"
                  value={draft.labelColumn ?? ''}
                />
              </label>
              <label>
                Tags · comma separated
                <input
                  className="dcinput"
                  onChange={(event) => { setTagsInput(event.target.value); }}
                  placeholder="site, address, customer"
                  value={tagsInput}
                />
              </label>
            </div>
            <div className="entity-relations-react">
              <header>
                <strong>Relations</strong>
                <button
                  className="dcbtn"
                  onClick={() =>
                    { setDraft({
                      ...draft,
                      relations: [
                        ...draft.relations,
                        { column: '', targetEntity: '', targetColumn: '' },
                      ],
                    }); }
                  }
                  type="button"
                >
                  ＋ Relation
                </button>
              </header>
              {draft.relations.map((relation, index) => (
                <div className="entity-relation-row-react" key={index}>
                  <input
                    aria-label={`Relation ${String(index + 1)} column`}
                    className="dcinput"
                    onChange={(event) => {
                      const relations = draft.relations.map((item, relationIndex) =>
                        relationIndex === index ? { ...item, column: event.target.value } : item,
                      )
                      setDraft({ ...draft, relations })
                    }}
                    placeholder="column"
                    value={relation.column}
                  />
                  <span>↔</span>
                  <select
                    aria-label={`Relation ${String(index + 1)} target entity`}
                    className="dcinput"
                    onChange={(event) => {
                      const relations = draft.relations.map((item, relationIndex) =>
                        relationIndex === index ? { ...item, targetEntity: event.target.value } : item,
                      )
                      setDraft({ ...draft, relations })
                    }}
                    value={relation.targetEntity}
                  >
                    <option value="">— entity —</option>
                    {data.entities
                      .filter((entity) => entity.id !== draft.id)
                      .map((entity) => (
                        <option key={entity.id} value={entity.name}>
                          {entity.name}
                        </option>
                      ))}
                  </select>
                  <input
                    aria-label={`Relation ${String(index + 1)} target column`}
                    className="dcinput"
                    onChange={(event) => {
                      const relations = draft.relations.map((item, relationIndex) =>
                        relationIndex === index ? { ...item, targetColumn: event.target.value } : item,
                      )
                      setDraft({ ...draft, relations })
                    }}
                    placeholder="target column"
                    value={relation.targetColumn}
                  />
                  <button
                    aria-label={`Remove relation ${String(index + 1)}`}
                    className="dcbtn"
                    onClick={() =>
                      { setDraft({
                        ...draft,
                        relations: draft.relations.filter(
                          (_, relationIndex) => relationIndex !== index,
                        ),
                      }); }
                    }
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
              <button className="dcbtn" onClick={() => { setDraft(null); }} type="button">
                Cancel
              </button>
              <button
                className="dcbtn primary"
                disabled={saveMutation.isPending}
                onClick={() => void save()}
                type="button"
              >
                Save entity
              </button>
            </footer>
          </section>
        </div>
      )}
    </div>
  )
}
