import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { type SyntheticEvent, useMemo, useState } from 'react'
import { NavLink, useSearchParams } from 'react-router-dom'

import { workspaceProjectsQuery } from '../../../workspace/api/workspace-query-options'
import {
  artifactDownloadUrl,
  createArtifactShare,
  deleteArtifacts,
  fetchArtifactShareStatus,
  fetchArtifactText,
  revokeArtifactShare,
} from '../../api/artifacts-api'
import { artifactsPageQueryOptions } from '../../api/artifacts-query'
import type { ArtifactCategory, ArtifactFile } from '../../model/artifacts'

const PAGE_SIZE = 25
const MAX_INLINE_BYTES = 2 * 1024 * 1024
const OTHER_CATEGORY = '__other'

export function ArtifactsPage() {
  const projectsQuery = useSuspenseQuery(workspaceProjectsQuery)
  const [searchParams, setSearchParams] = useSearchParams()
  const initialProjectId = searchParams.has('artifact') ? '' : (projectsQuery.data[0]?.id ?? '')
  const [projectId, setProjectId] = useState(initialProjectId)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [page, setPage] = useState(1)
  const [expanded, setExpanded] = useState<string | null>(null)
  const [selected, setSelected] = useState<Set<string>>(() => new Set())
  const [activeId, setActiveId] = useState<string | null>(() => searchParams.get('artifact'))
  const [confirmDelete, setConfirmDelete] = useState(false)
  const pageQuery = useSuspenseQuery(artifactsPageQueryOptions(projectId, search))
  const queryClient = useQueryClient()

  const filtered = useMemo(
    () =>
      filterArtifacts(
        pageQuery.data.files,
        searchInput,
        category,
        pageQuery.data.config.categories,
      ),
    [category, pageQuery.data.config.categories, pageQuery.data.files, searchInput],
  )
  const groups = useMemo(() => groupArtifacts(filtered), [filtered])
  const maxPage = Math.max(1, Math.ceil(groups.length / PAGE_SIZE))
  const visibleGroups = groups.slice(
    (Math.min(page, maxPage) - 1) * PAGE_SIZE,
    Math.min(page, maxPage) * PAGE_SIZE,
  )
  const active = pageQuery.data.files.find((artifact) => artifact.id === activeId)
  const deleteMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      const controller = new AbortController()
      return deleteArtifacts(ids, controller.signal)
    },
    onSuccess: async () => {
      setSelected(new Set())
      setActiveId(null)
      setConfirmDelete(false)
      setSearchParams(
        (current) => {
          current.delete('artifact')
          return current
        },
        { replace: true },
      )
      await queryClient.invalidateQueries({ queryKey: ['artifacts-page'] })
    },
  })

  function submitSearch(event: SyntheticEvent<HTMLFormElement>): void {
    event.preventDefault()
    setSearch(searchInput.trim())
    setPage(1)
  }

  function selectProject(nextProjectId: string): void {
    setProjectId(nextProjectId)
    setSearch('')
    setSearchInput('')
    setCategory('')
    setPage(1)
    setActiveId(null)
    setSelected(new Set())
  }

  function openArtifact(artifact: ArtifactFile): void {
    setActiveId(artifact.id)
    setConfirmDelete(false)
    setSearchParams({ artifact: artifact.id }, { replace: true })
  }

  function closeArtifact(): void {
    setActiveId(null)
    setConfirmDelete(false)
    setSearchParams({}, { replace: true })
  }

  function toggleArtifact(id: string, checked: boolean): void {
    setSelected((current) => {
      const next = new Set(current)
      if (checked) next.add(id)
      else next.delete(id)
      return next
    })
  }

  function toggleGroup(group: ArtifactFile[], checked: boolean): void {
    setSelected((current) => {
      const next = new Set(current)
      for (const artifact of group) {
        if (checked) next.add(artifact.id)
        else next.delete(artifact.id)
      }
      return next
    })
  }

  return (
    <section className="artifacts-page-react">
      <aside className={active === undefined ? 'artifacts-files' : 'artifacts-files viewer-open'}>
        <header>
          <div>
            <strong>Artifact files</strong>
            <form onSubmit={submitSearch}>
              <input
                aria-label="Search files"
                onChange={(event) => {
                  setSearchInput(event.target.value)
                  setPage(1)
                }}
                placeholder="search files…"
                value={searchInput}
              />
            </form>
          </div>
          <select
            aria-label="Project"
            onChange={(event) => {
              selectProject(event.target.value)
            }}
            value={projectId}
          >
            <option value="">All projects</option>
            {pageQuery.data.projects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.name}
              </option>
            ))}
          </select>
          <CategoryFilters
            categories={pageQuery.data.config.categories}
            files={pageQuery.data.files}
            onChange={(next) => {
              setCategory(next)
              setPage(1)
              setSelected(new Set())
            }}
            selected={category}
            settingsLink={pageQuery.data.canManageSettings}
          />
          {pageQuery.data.canDelete && selected.size > 0 ? (
            <div className="artifacts-bulk-bar">
              <span>{selected.size} selected</span>
              <button
                className="dcbtn danger xs"
                disabled={deleteMutation.isPending}
                onClick={() => {
                  deleteMutation.mutate([...selected])
                }}
                type="button"
              >
                {deleteMutation.isPending ? 'Deleting…' : 'Delete selected'}
              </button>
              <button
                className="dcbtn xs"
                onClick={() => {
                  setSelected(new Set())
                }}
                type="button"
              >
                Clear
              </button>
            </div>
          ) : null}
          {pageQuery.data.loadMayBeIncomplete ? (
            <p className="artifacts-cap-note">
              Showing the newest loaded files. Narrow the project or search to find older artifacts.
            </p>
          ) : null}
        </header>

        <div className="artifacts-file-list">
          {groups.length === 0 ? (
            <div className="artifacts-empty">No artifact files match the selected filters.</div>
          ) : null}
          {visibleGroups.map((group) => {
            const latest = group[0]
            if (latest === undefined) return null
            const key = groupKey(latest)
            const groupActive = group.some((artifact) => artifact.id === activeId)
            return (
              <article className={groupActive ? 'active' : undefined} key={key}>
                <div
                  className="artifacts-file-row"
                  onClick={() => {
                    openArtifact(latest)
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') openArtifact(latest)
                  }}
                  role="button"
                  tabIndex={0}
                >
                  {pageQuery.data.canDelete ? (
                    <input
                      aria-label={`Select ${latest.title}`}
                      checked={group.every((artifact) => selected.has(artifact.id))}
                      onChange={(event) => {
                        toggleGroup(group, event.target.checked)
                      }}
                      onClick={(event) => {
                        event.stopPropagation()
                      }}
                      type="checkbox"
                    />
                  ) : null}
                  <span className="artifacts-file-icon" aria-hidden="true">
                    {fileLabel(latest)}
                  </span>
                  <div>
                    <strong>{latest.title}</strong>
                    <small>
                      {projectName(pageQuery.data.projects, latest.projectId)} · {latest.kind} ·{' '}
                      {bytes(latest.sizeBytes)} · {formatDate(latest.createdAt)}
                    </small>
                  </div>
                  {group.length > 1 ? (
                    <button
                      aria-label={`Show ${String(group.length)} versions of ${latest.title}`}
                      onClick={(event) => {
                        event.stopPropagation()
                        setExpanded((current) => (current === key ? null : key))
                      }}
                      type="button"
                    >
                      v{group.length} {expanded === key ? '▴' : '▾'}
                    </button>
                  ) : null}
                </div>
                {expanded === key
                  ? group.slice(1).map((artifact, index) => (
                      <div
                        className={
                          artifact.id === activeId
                            ? 'artifacts-version active'
                            : 'artifacts-version'
                        }
                        key={artifact.id}
                      >
                        {pageQuery.data.canDelete ? (
                          <input
                            aria-label={`Select version ${String(group.length - index - 1)} of ${latest.title}`}
                            checked={selected.has(artifact.id)}
                            onChange={(event) => {
                              toggleArtifact(artifact.id, event.target.checked)
                            }}
                            type="checkbox"
                          />
                        ) : null}
                        <button
                          onClick={() => {
                            openArtifact(artifact)
                          }}
                          type="button"
                        >
                          <span>v{group.length - index - 1}</span>
                          <span>{formatDate(artifact.createdAt)}</span>
                          <span>{bytes(artifact.sizeBytes)}</span>
                        </button>
                      </div>
                    ))
                  : null}
              </article>
            )
          })}
        </div>

        {groups.length > 0 ? (
          <footer>
            <span>
              Showing {(Math.min(page, maxPage) - 1) * PAGE_SIZE + 1}–
              {Math.min(Math.min(page, maxPage) * PAGE_SIZE, groups.length)} of {groups.length}{' '}
              files
            </span>
            <div>
              <button
                className="dcbtn xs"
                disabled={page <= 1}
                onClick={() => {
                  setPage((current) => Math.max(1, current - 1))
                }}
                type="button"
              >
                ‹ Prev
              </button>
              <span>
                Page {Math.min(page, maxPage)} of {maxPage}
              </span>
              <button
                className="dcbtn xs"
                disabled={page >= maxPage}
                onClick={() => {
                  setPage((current) => Math.min(maxPage, current + 1))
                }}
                type="button"
              >
                Next ›
              </button>
            </div>
          </footer>
        ) : null}
      </aside>

      <main className={active === undefined ? 'artifacts-viewer empty' : 'artifacts-viewer'}>
        {active === undefined ? (
          <div>
            <span aria-hidden="true">◇</span>
            <p>Select a file on the left to view it.</p>
          </div>
        ) : (
          <>
            <header>
              <button className="artifacts-back" onClick={closeArtifact} type="button">
                ← Back
              </button>
              <strong>{active.title}</strong>
              <span>{active.contentType}</span>
              <a
                className="dcbtn xs"
                href={artifactDownloadUrl(active)}
                rel="noopener"
                target="_blank"
              >
                Open ↗
              </a>
              {pageQuery.data.canShare ? <ArtifactShare artifact={active} /> : null}
              {pageQuery.data.canDelete ? (
                confirmDelete ? (
                  <>
                    <button
                      className="dcbtn danger xs"
                      disabled={deleteMutation.isPending}
                      onClick={() => {
                        deleteMutation.mutate([active.id])
                      }}
                      type="button"
                    >
                      Confirm delete
                    </button>
                    <button
                      className="dcbtn xs"
                      onClick={() => {
                        setConfirmDelete(false)
                      }}
                      type="button"
                    >
                      Keep
                    </button>
                  </>
                ) : (
                  <button
                    className="dcbtn xs"
                    onClick={() => {
                      setConfirmDelete(true)
                    }}
                    type="button"
                  >
                    Delete
                  </button>
                )
              ) : null}
            </header>
            <ArtifactPreview artifact={active} />
          </>
        )}
      </main>
    </section>
  )
}

function CategoryFilters({
  categories,
  files,
  onChange,
  selected,
  settingsLink,
}: {
  categories: ArtifactCategory[]
  files: ArtifactFile[]
  onChange: (category: string) => void
  selected: string
  settingsLink: boolean
}) {
  const otherCount = files.filter((file) => categoryFor(file, categories) === null).length
  return (
    <nav className="artifacts-categories" aria-label="Artifact type filters">
      <button
        className={selected === '' ? 'active' : undefined}
        onClick={() => {
          onChange('')
        }}
        type="button"
      >
        All <small>{files.length}</small>
      </button>
      {categories.map((category) => (
        <button
          className={selected === category.id ? 'active' : undefined}
          key={category.id}
          onClick={() => {
            onChange(category.id)
          }}
          title={`Prefixes: ${category.prefixes.join(', ')}`}
          type="button"
        >
          {category.label}{' '}
          <small>
            {files.filter((file) => categoryFor(file, categories) === category.id).length}
          </small>
        </button>
      ))}
      {otherCount > 0 ? (
        <button
          className={selected === OTHER_CATEGORY ? 'active' : undefined}
          onClick={() => {
            onChange(OTHER_CATEGORY)
          }}
          type="button"
        >
          Other <small>{otherCount}</small>
        </button>
      ) : null}
      {settingsLink ? (
        <NavLink aria-label="Configure artifact filters" to="/settings/artifacts">
          ⚙
        </NavLink>
      ) : null}
    </nav>
  )
}

function ArtifactPreview({ artifact }: { artifact: ArtifactFile }) {
  const textPreview = isTextData(artifact) && artifact.sizeBytes <= MAX_INLINE_BYTES
  const textQuery = useQuery({
    queryKey: ['artifact-preview', artifact.id, artifact.createdAt],
    queryFn: ({ signal }) => fetchArtifactText(artifact, signal),
    enabled: textPreview,
  })
  const url = artifactDownloadUrl(artifact)

  if (textPreview) {
    if (textQuery.isPending) return <div className="artifacts-preview-note">Loading preview…</div>
    if (textQuery.error instanceof Error)
      return <div className="artifacts-preview-note error">{textQuery.error.message}</div>
    if (isJson(artifact))
      return <pre className="artifacts-json">{prettyJson(textQuery.data ?? '')}</pre>
    return <CsvPreview text={textQuery.data ?? ''} />
  }
  if (isImage(artifact))
    return (
      <div className="artifacts-image">
        <img alt={artifact.title} src={url} />
      </div>
    )
  if (artifact.contentType === 'application/pdf')
    return <iframe className="artifacts-frame" src={url} title={artifact.title} />
  if (previewable(artifact))
    return <iframe className="artifacts-frame" src={url} title={artifact.title} />
  return (
    <div className="artifacts-preview-note">
      No inline preview for {artifact.contentType} — use Open ↗ to download it.
    </div>
  )
}

function CsvPreview({ text }: { text: string }) {
  const rows = parseCsv(text).slice(0, 501)
  const header = rows[0] ?? []
  return (
    <div className="artifacts-csv">
      <table>
        <thead>
          <tr>
            {header.map((cell, index) => (
              <th key={`${cell}-${String(index)}`}>{cell}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.slice(1).map((row, rowIndex) => (
            <tr key={String(rowIndex)}>
              {row.map((cell, cellIndex) => (
                <td key={String(cellIndex)}>{cell}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length >= 501 ? <p>Showing the first 500 rows — Open ↗ for the full file.</p> : null}
    </div>
  )
}

function ArtifactShare({ artifact }: { artifact: ArtifactFile }) {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [lifetimeDays, setLifetimeDays] = useState(7)
  const [createdUrl, setCreatedUrl] = useState<string | null>(null)
  const statusQuery = useQuery({
    queryKey: ['artifact-share', artifact.id],
    queryFn: ({ signal }) => fetchArtifactShareStatus(artifact.id, signal),
    enabled: open,
  })
  const createMutation = useMutation({
    mutationFn: async () => {
      const controller = new AbortController()
      return createArtifactShare(artifact.id, lifetimeDays, controller.signal)
    },
    onSuccess: async (created) => {
      setCreatedUrl(`${window.location.origin}/share/artifacts/${created.token}`)
      await queryClient.invalidateQueries({ queryKey: ['artifact-share', artifact.id] })
    },
  })
  const revokeMutation = useMutation({
    mutationFn: async () => {
      const controller = new AbortController()
      return revokeArtifactShare(artifact.id, controller.signal)
    },
    onSuccess: async () => {
      setCreatedUrl(null)
      await queryClient.invalidateQueries({ queryKey: ['artifact-share', artifact.id] })
    },
  })

  return (
    <div className="artifacts-share-wrap">
      <button
        className="dcbtn xs"
        onClick={() => {
          setOpen((current) => !current)
        }}
        type="button"
      >
        {open ? 'Close share' : 'Share'}
      </button>
      {open ? (
        <div className="artifacts-share-panel">
          <strong>Public share link</strong>
          <p>Anyone with the code can open this artifact. Links expire and can be revoked.</p>
          {createdUrl !== null ? (
            <div>
              <input aria-label="Public share link" readOnly value={createdUrl} />
              <button
                className="dcbtn xs"
                onClick={() => {
                  void navigator.clipboard.writeText(createdUrl)
                }}
                type="button"
              >
                Copy link
              </button>
            </div>
          ) : statusQuery.data?.isActive === true ? (
            <div>
              <span>
                Active code {statusQuery.data.tokenPrefix} · expires{' '}
                {formatDate(statusQuery.data.expiresAt)}
              </span>
              <button
                className="dcbtn xs"
                disabled={revokeMutation.isPending}
                onClick={() => {
                  revokeMutation.mutate()
                }}
                type="button"
              >
                Revoke
              </button>
            </div>
          ) : (
            <div>
              <label>
                Expires after{' '}
                <select
                  onChange={(event) => {
                    setLifetimeDays(Number(event.target.value))
                  }}
                  value={lifetimeDays}
                >
                  <option value="1">1 day</option>
                  <option value="7">7 days</option>
                  <option value="30">30 days</option>
                </select>
              </label>
              <button
                className="dcbtn xs"
                disabled={createMutation.isPending}
                onClick={() => {
                  createMutation.mutate()
                }}
                type="button"
              >
                Create public link
              </button>
            </div>
          )}
        </div>
      ) : null}
    </div>
  )
}

function filterArtifacts(
  files: ArtifactFile[],
  search: string,
  category: string,
  categories: ArtifactCategory[],
): ArtifactFile[] {
  const query = search.trim().toLowerCase()
  return files.filter((file) => {
    if (
      query !== '' &&
      !file.title.toLowerCase().includes(query) &&
      !file.kind.toLowerCase().includes(query)
    )
      return false
    const matched = categoryFor(file, categories)
    if (category === OTHER_CATEGORY) return matched === null
    return category === '' || matched === category
  })
}

function categoryFor(file: ArtifactFile, categories: ArtifactCategory[]): string | null {
  return (
    categories.find((category) =>
      category.prefixes.some(
        (prefix) =>
          prefix.trim() !== '' && file.title.toLowerCase().startsWith(prefix.toLowerCase()),
      ),
    )?.id ?? null
  )
}

function groupArtifacts(files: ArtifactFile[]): ArtifactFile[][] {
  const grouped = new Map<string, ArtifactFile[]>()
  for (const file of files)
    grouped.set(groupKey(file), [...(grouped.get(groupKey(file)) ?? []), file])
  return [...grouped.values()]
    .map((group) => group.toSorted((a, b) => b.createdAt.localeCompare(a.createdAt)))
    .toSorted((a, b) => (b[0]?.createdAt ?? '').localeCompare(a[0]?.createdAt ?? ''))
}

function groupKey(file: ArtifactFile): string {
  return `${file.projectId}|${file.kind}|${file.title}`
}
function projectName(projects: { id: string; name: string }[], id: string): string {
  return projects.find((project) => project.id === id)?.name ?? '—'
}
function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(value),
  )
}
function bytes(value: number): string {
  if (value < 1024) return `${String(value)} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  return `${(value / (1024 * 1024)).toFixed(1)} MB`
}
function fileLabel(file: ArtifactFile): string {
  if (isImage(file)) return 'IMG'
  if (isJson(file)) return 'JSON'
  if (file.contentType.includes('csv')) return 'CSV'
  if (file.contentType === 'application/pdf') return 'PDF'
  return 'FILE'
}
function isJson(file: ArtifactFile): boolean {
  return file.contentType.toLowerCase().includes('json')
}
function isTextData(file: ArtifactFile): boolean {
  return isJson(file) || file.contentType.toLowerCase().includes('csv')
}
function isImage(file: ArtifactFile): boolean {
  return (
    file.contentType.startsWith('image/') ||
    /\.(png|jpe?g|gif|webp|avif|bmp|tiff?|svg)$/i.test(file.title)
  )
}
function previewable(file: ArtifactFile): boolean {
  return (
    file.contentType.startsWith('text/') ||
    file.contentType.startsWith('video/') ||
    file.contentType.includes('svg')
  )
}
function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text) as unknown, null, 2)
  } catch {
    return text
  }
}

function parseCsv(text: string): string[][] {
  return text
    .split(/\r?\n/)
    .filter((line) => line !== '')
    .map((line) => {
      const cells: string[] = []
      let value = ''
      let quoted = false
      for (let index = 0; index < line.length; index += 1) {
        const character = line[index]
        if (quoted && character === '"' && line[index + 1] === '"') {
          value += '"'
          index += 1
        } else if (character === '"') quoted = !quoted
        else if (character === ',' && !quoted) {
          cells.push(value)
          value = ''
        } else value += character ?? ''
      }
      cells.push(value)
      return cells
    })
}
