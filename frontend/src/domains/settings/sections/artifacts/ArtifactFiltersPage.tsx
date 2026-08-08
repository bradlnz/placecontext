import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { artifactFiltersQueryOptions } from '../../api/settings-queries'
import { resetArtifactFilters, saveArtifactFilters } from '../../api/settings-api'
import type { ArtifactFilter } from '../../model/settings'

function parsePrefixes(value: string): string[] {
  return [
    ...new Set(
      value
        .split(/[\n,\r]/u)
        .map((prefix) => prefix.trim())
        .filter(Boolean),
    ),
  ]
}

export function ArtifactFiltersPage() {
  const { data } = useSuspenseQuery(artifactFiltersQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [categories, setCategories] = useState(data.categories)
  const [message, setMessage] = useState<string | null>(null)
  const saveMutation = useMutation({
    mutationFn: async (value: ArtifactFilter[]) =>
      saveArtifactFilters({ categories: value }, AbortSignal.timeout(30_000)),
  })
  const resetMutation = useMutation({
    mutationFn: async () => resetArtifactFilters(AbortSignal.timeout(30_000)),
  })

  useEffect(() => {
    const unsubscribeSave = eventBus.subscribe(
      'settings.artifact-filters-save-requested',
      async ({ categories: value }) => {
        setMessage(null)
        if (
          value.some((category) => category.label.trim() === '' || category.prefixes.length === 0)
        ) {
          setMessage('Every filter needs a button label and at least one filename prefix.')
          return
        }
        try {
          const saved = await saveMutation.mutateAsync(value)
          setCategories(saved.categories)
          queryClient.setQueryData(artifactFiltersQueryOptions.queryKey, saved)
          setMessage('Artifact filters saved.')
          await eventBus.publish('settings.artifact-filters-saved', {
            categories: saved.categories,
          })
        } catch (error: unknown) {
          setMessage(
            error instanceof Error ? error.message : 'Artifact filters could not be saved.',
          )
        }
      },
    )
    const unsubscribeReset = eventBus.subscribe(
      'settings.artifact-filters-reset-requested',
      async () => {
        try {
          const defaults = await resetMutation.mutateAsync()
          setCategories(defaults.categories)
          setMessage('Defaults restored locally. Save to apply them.')
        } catch (error: unknown) {
          setMessage(error instanceof Error ? error.message : 'Defaults could not be loaded.')
        }
      },
    )
    return () => {
      unsubscribeSave()
      unsubscribeReset()
    }
  }, [eventBus, queryClient, resetMutation, saveMutation])

  async function addFilter(): Promise<void> {
    await Promise.resolve()
    setCategories((current) => [
      ...current,
      { id: `category-${crypto.randomUUID().replaceAll('-', '')}`, label: '', prefixes: [] },
    ])
  }

  async function moveFilter(index: number, delta: number): Promise<void> {
    await Promise.resolve()
    setCategories((current) => {
      const target = index + delta
      if (target < 0 || target >= current.length) return current
      const next = [...current]
      const item = next[index]
      const targetItem = next[target]
      if (item === undefined || targetItem === undefined) return current
      next[index] = targetItem
      next[target] = item
      return next
    })
  }

  function updateFilter(index: number, update: Partial<ArtifactFilter>): void {
    setCategories((current) =>
      current.map((category, itemIndex) =>
        itemIndex === index ? { ...category, ...update } : category,
      ),
    )
  }

  const busy = saveMutation.isPending || resetMutation.isPending

  return (
    <div className="settings-page artifact-filters-page">
      <title>placecontext — Artifact filters</title>
      <h1>Artifact filters</h1>
      <p className="settings-intro">
        Group artifact files into named filter buttons using filename prefixes. Rules are checked
        from top to bottom.
      </p>
      {message === null ? null : (
        <div className="settings-message" role="status">
          {message}
        </div>
      )}
      <section className="dccard artifact-rules-card">
        <header>
          <div>
            <h2>Filter buttons</h2>
            <p>
              Example: <code>feasibility_v1_</code> → <strong>Feasibility Reports</strong>
            </p>
          </div>
          <button
            className="dcbtn"
            disabled={busy}
            onClick={() => void eventBus.publish('settings.artifact-filters-reset-requested', {})}
            type="button"
          >
            Reset defaults
          </button>
        </header>
        {categories.length === 0 ? (
          <div className="token-empty">
            No custom artifact filters. The page will show All and Other.
          </div>
        ) : (
          <div>
            {categories.map((category, index) => (
              <article className="artifact-rule" key={category.id}>
                <div className="menu-move">
                  <button
                    aria-label={`Move ${category.label || 'filter'} up`}
                    className="dcbtn"
                    disabled={index === 0}
                    onClick={() => {
                      void moveFilter(index, -1)
                    }}
                    type="button"
                  >
                    ↑
                  </button>
                  <button
                    aria-label={`Move ${category.label || 'filter'} down`}
                    className="dcbtn"
                    disabled={index === categories.length - 1}
                    onClick={() => {
                      void moveFilter(index, 1)
                    }}
                    type="button"
                  >
                    ↓
                  </button>
                </div>
                <div className="artifact-rule-fields">
                  <label className="dcfield">
                    <span>Button label</span>
                    <input
                      onChange={(event) => {
                        updateFilter(index, { label: event.target.value })
                      }}
                      value={category.label}
                    />
                  </label>
                  <label className="dcfield">
                    <span>Filename prefixes</span>
                    <textarea
                      onChange={(event) => {
                        updateFilter(index, { prefixes: parsePrefixes(event.target.value) })
                      }}
                      value={category.prefixes.join('\n')}
                    />
                  </label>
                </div>
                <button
                  className="dcbtn"
                  onClick={() => {
                    setCategories((current) => current.filter((item) => item.id !== category.id))
                  }}
                  type="button"
                >
                  Remove
                </button>
              </article>
            ))}
          </div>
        )}
        <button
          className="dcbtn artifact-add"
          onClick={() => {
            void addFilter()
          }}
          type="button"
        >
          ＋ Add filter
        </button>
      </section>
      <div className="settings-actions">
        <button
          className="dcbtn primary"
          disabled={busy}
          onClick={() =>
            void eventBus.publish('settings.artifact-filters-save-requested', { categories })
          }
          type="button"
        >
          {saveMutation.isPending ? 'Saving…' : 'Save artifact filters'}
        </button>
      </div>
    </div>
  )
}
