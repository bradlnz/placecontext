import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { menuQueryOptions } from '../../api/settings-queries'
import { resetMenu, saveMenu } from '../../api/settings-api'
import type { MenuSettingsItem } from '../../model/settings'

export function MenuPage() {
  const { data } = useSuspenseQuery(menuQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [workspace, setWorkspace] = useState(data.workspace)
  const [message, setMessage] = useState<string | null>(null)
  const saveMutation = useMutation({
    mutationFn: async (items: MenuSettingsItem[]) => saveMenu(items, AbortSignal.timeout(30_000)),
  })
  const resetMutation = useMutation({
    mutationFn: async () => resetMenu(AbortSignal.timeout(30_000)),
  })

  useEffect(() => {
    const unsubscribeSave = eventBus.subscribe(
      'settings.menu-save-requested',
      async ({ workspace: items }) => {
        setMessage(null)
        try {
          const saved = await saveMutation.mutateAsync(items)
          setWorkspace(saved.workspace)
          queryClient.setQueryData(menuQueryOptions.queryKey, saved)
          setMessage('Menu saved. Reload the page to refresh the sidebars.')
          await eventBus.publish('settings.menu-saved', { workspace: saved.workspace })
        } catch (error: unknown) {
          setMessage(error instanceof Error ? error.message : 'The menu could not be saved.')
        }
      },
    )
    const unsubscribeReset = eventBus.subscribe('settings.menu-reset-requested', async () => {
      setMessage(null)
      try {
        const reset = await resetMutation.mutateAsync()
        setWorkspace(reset.workspace)
        queryClient.setQueryData(menuQueryOptions.queryKey, reset)
        setMessage('Menu reset to defaults.')
      } catch (error: unknown) {
        setMessage(error instanceof Error ? error.message : 'The menu could not be reset.')
      }
    })
    return () => {
      unsubscribeSave()
      unsubscribeReset()
    }
  }, [eventBus, queryClient, resetMutation, saveMutation])

  function updateItem(index: number, update: Partial<MenuSettingsItem>): void {
    setWorkspace((current) =>
      current.map((item, itemIndex) => (itemIndex === index ? { ...item, ...update } : item)),
    )
  }

  async function move(index: number, delta: number): Promise<void> {
    await Promise.resolve()
    setWorkspace((current) => {
      const target = index + delta
      if (target < 0 || target >= current.length) return current
      const next = [...current]
      const item = next[index]
      const targetItem = next[target]
      if (item === undefined || targetItem === undefined) return current
      next[index] = targetItem
      next[target] = item
      return next.map((entry, order) => ({ ...entry, order: order * 10 }))
    })
  }

  const busy = saveMutation.isPending || resetMutation.isPending

  return (
    <div className="settings-page menu-page">
      <title>placecontext — Menu</title>
      <header className="settings-page-head">
        <div>
          <span className="settings-kicker">Workspace navigation</span>
          <h1>Menu</h1>
          <p>Reorder, rename and group sidebar items. Permission rules still control visibility.</p>
        </div>
        <div className="settings-actions">
          <button
            className="dcbtn"
            disabled={busy}
            onClick={() => void eventBus.publish('settings.menu-reset-requested', {})}
            type="button"
          >
            Reset defaults
          </button>
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() => void eventBus.publish('settings.menu-save-requested', { workspace })}
            type="button"
          >
            {saveMutation.isPending ? 'Saving…' : 'Save menu'}
          </button>
        </div>
      </header>
      {message === null ? null : (
        <div className="settings-message" role="status">
          {message}
        </div>
      )}
      <section aria-label="Sidebar items" className="dccard menu-card">
        <div className="menu-card-head">
          <strong>Sidebar items</strong>
          <span>{workspace.length} items</span>
        </div>
        {workspace.map((item, index) => (
          <article className={`menu-settings-row${item.visible ? '' : ' muted'}`} key={item.id}>
            <div className="menu-move">
              <button
                aria-label={`Move ${item.defaultLabel} up`}
                className="dcbtn"
                disabled={index === 0}
                onClick={() => {
                  void move(index, -1)
                }}
                type="button"
              >
                ↑
              </button>
              <button
                aria-label={`Move ${item.defaultLabel} down`}
                className="dcbtn"
                disabled={index === workspace.length - 1}
                onClick={() => {
                  void move(index, 1)
                }}
                type="button"
              >
                ↓
              </button>
            </div>
            <div className="menu-item-fields">
              <div className="menu-item-head">
                <strong>{item.defaultLabel}</strong>
                <code>{item.id}</code>
              </div>
              <div className="settings-field-grid two-columns">
                <label className="dcfield">
                  <span>Label</span>
                  <input
                    onChange={(event) => {
                      updateItem(index, { label: event.target.value })
                    }}
                    placeholder="Use default"
                    value={item.label}
                  />
                </label>
                <label className="dcfield">
                  <span>Section</span>
                  <input
                    onChange={(event) => {
                      updateItem(index, { section: event.target.value })
                    }}
                    placeholder="No section heading"
                    value={item.section}
                  />
                </label>
              </div>
            </div>
            <label className="menu-visible">
              <input
                checked={item.visible}
                onChange={(event) => {
                  updateItem(index, { visible: event.target.checked })
                }}
                type="checkbox"
              />{' '}
              Visible
            </label>
          </article>
        ))}
      </section>
    </div>
  )
}
