import { useMutation } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { importBackupManifest, readBackupManifest } from '../../api/backup-api'
import type { BackupImportResult, BackupManifestPreview } from '../../model/backup'

export function BackupPage() {
  const eventBus = useAppEventBus()
  const [pending, setPending] = useState<BackupManifestPreview | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [result, setResult] = useState<BackupImportResult | null>(null)
  const importMutation = useMutation({
    mutationFn: async (manifest: Record<string, unknown>) =>
      importBackupManifest(manifest, AbortSignal.timeout(60_000)),
  })

  useEffect(
    () =>
      eventBus.subscribe('settings.backup-import-requested', async ({ manifest }) => {
        setMessage(null)
        try {
          const imported = await importMutation.mutateAsync(manifest)
          setResult(imported)
          setConfirming(false)
          setMessage('Import complete.')
          await eventBus.publish('settings.backup-imported', {
            projectsCreated: imported.projectsCreated,
            jobsCreated: imported.jobsCreated,
          })
        } catch (error: unknown) {
          setMessage(`Import failed: ${error instanceof Error ? error.message : 'Unknown error.'}`)
        }
      }),
    [eventBus, importMutation],
  )

  async function handleFileSelected(file: File | undefined): Promise<void> {
    setPending(null)
    setResult(null)
    setConfirming(false)
    setMessage(null)
    if (file === undefined) return

    try {
      setPending(await readBackupManifest(file))
    } catch (error: unknown) {
      setMessage(
        `Couldn't read that file as a backup manifest: ${error instanceof Error ? error.message : 'Unknown error.'}`,
      )
    }
  }

  async function handleImport(): Promise<void> {
    if (pending === null) return
    await eventBus.publish('settings.backup-import-requested', { manifest: pending.manifest })
  }

  async function handleConfirmationChanged(value: boolean): Promise<void> {
    await Promise.resolve()
    setConfirming(value)
  }

  return (
    <div className="settings-page backup-page">
      <title>placecontext — Backup</title>
      <h1>Backup &amp; restore</h1>
      <p className="settings-intro">
        Export this workspace&apos;s settings and job definitions to a single JSON manifest, or
        import one to bring a workspace up to date. Run history and vault secrets are never
        included.
      </p>
      <div className="backup-export-grid">
        <article className="dccard settings-card">
          <h2>Export backup</h2>
          <p>
            Downloads projects, jobs, chains, triggers, event types, data mappings, and workspace
            settings as one JSON file.
          </p>
          <a className="dcbtn primary" href="/backup/export">
            Download backup
          </a>
        </article>
        <article className="dccard settings-card">
          <h2>Export job code</h2>
          <p>Downloads every job&apos;s map and reduce source files as a ZIP.</p>
          <a className="dcbtn primary" href="/backup/jobs-code">
            Download job code
          </a>
        </article>
      </div>
      <section className="dccard settings-card backup-import-card">
        <h2>Import backup</h2>
        <p>Merges a manifest into this workspace; re-importing the same file is safe.</p>
        <label className="dcfield">
          <span>Backup manifest</span>
          <input
            accept="application/json"
            onChange={(event) => void handleFileSelected(event.target.files?.[0])}
            type="file"
          />
        </label>
        {pending === null ? null : (
          <p>
            Loaded <code>{pending.fileName}</code> — {pending.projectCount} project(s),{' '}
            {pending.jobCount} job(s), {pending.chainCount} chain(s).
          </p>
        )}
        {pending === null ? null : confirming ? (
          <div className="settings-actions">
            <button
              className="dcbtn primary"
              disabled={importMutation.isPending}
              onClick={() => void handleImport()}
              type="button"
            >
              {importMutation.isPending ? 'Importing…' : 'Confirm import'}
            </button>
            <button
              className="dcbtn"
              disabled={importMutation.isPending}
              onClick={() => void handleConfirmationChanged(false)}
              type="button"
            >
              Cancel
            </button>
          </div>
        ) : (
          <button
            className="dcbtn"
            onClick={() => void handleConfirmationChanged(true)}
            type="button"
          >
            Import into this workspace
          </button>
        )}
        {message === null ? null : (
          <div className="settings-message" role="status">
            {message}
          </div>
        )}
        {result === null ? null : (
          <div className="settings-hint">
            Projects: {result.projectsCreated} created / {result.projectsUpdated} updated · Jobs:{' '}
            {result.jobsCreated} created / {result.jobsUpdated} updated / {result.jobsSkipped}{' '}
            skipped
          </div>
        )}
      </section>
    </div>
  )
}
