import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { createClusterJoinCommand, fetchCluster } from '../../api/cluster-api'
import { clusterQueryOptions } from '../../api/cluster-query'

type ClusterCommand = { kind: 'refresh' } | { kind: 'join' }

export function ClusterPage() {
  const { data } = useSuspenseQuery(clusterQueryOptions)
  const queryClient = useQueryClient()
  const [message, setMessage] = useState<string | null>(null)
  const [messageOk, setMessageOk] = useState(true)
  const [joinCommand, setJoinCommand] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const mutation = useMutation({
    mutationFn: async (command: ClusterCommand) =>
      command.kind === 'refresh'
        ? fetchCluster(AbortSignal.timeout(30_000))
        : createClusterJoinCommand(AbortSignal.timeout(30_000)),
    onSuccess: (result, command) => {
      setMessageOk(true)
      if (command.kind === 'refresh' && typeof result !== 'string')
        queryClient.setQueryData(clusterQueryOptions.queryKey, result)
      if (command.kind === 'join' && typeof result === 'string') setJoinCommand(result)
    },
  })
  const readyCount = data.nodes.filter((node) => node.ready).length
  const readyPercent = data.nodes.length === 0 ? 0 : (readyCount * 100) / data.nodes.length

  async function execute(command: ClusterCommand): Promise<void> {
    setMessage(null)
    if (command.kind === 'join') {
      setJoinCommand(null)
      setCopied(false)
    }
    try {
      await mutation.mutateAsync(command)
    } catch (error: unknown) {
      setMessage(error instanceof Error ? error.message : 'The cluster request failed.')
      setMessageOk(false)
    }
  }

  async function copyCommand(): Promise<void> {
    if (joinCommand === null) return
    try {
      await navigator.clipboard.writeText(joinCommand)
      setCopied(true)
    } catch {
      setMessage('The join command could not be copied.')
      setMessageOk(false)
    }
  }

  async function dismissJoin(): Promise<void> {
    await Promise.resolve()
    setJoinCommand(null)
    setCopied(false)
  }

  return (
    <div className="cluster-page-react">
      <title>PlaceContext — Cluster</title>
      <section className="cluster-hero-react">
        <div>
          <div className="cluster-eyebrow">
            <span /> Infrastructure
          </div>
          <h1>Cluster overview</h1>
          <p>
            Monitor the machines running your workloads and grow the fleet when capacity changes.
          </p>
        </div>
        <div className="cluster-actions">
          <button
            aria-label="Refresh nodes"
            disabled={mutation.isPending}
            onClick={() => void execute({ kind: 'refresh' })}
            title="Refresh nodes"
            type="button"
          >
            <span
              className={mutation.isPending && mutation.variables.kind === 'refresh' ? 'spin' : ''}
            >
              ↻
            </span>
          </button>
          <button
            disabled={mutation.isPending}
            onClick={() => void execute({ kind: 'join' })}
            type="button"
          >
            <span>+</span> Add worker
          </button>
        </div>
      </section>
      {message === null ? null : (
        <div
          className={messageOk ? 'cluster-message' : 'cluster-message error'}
          role={messageOk ? 'status' : 'alert'}
        >
          {message}
        </div>
      )}
      <section className="cluster-metrics">
        <article>
          <i>⌘</i>
          <div>
            <span>Total nodes</span>
            <strong>{data.nodes.length}</strong>
            <small>{data.isRealCluster ? 'Kubernetes fleet' : 'Local environment'}</small>
          </div>
        </article>
        <article>
          <i>✓</i>
          <div>
            <span>Healthy</span>
            <strong>{readyCount}</strong>
            <small>{readyPercent.toFixed(0)}% ready</small>
          </div>
        </article>
        <article>
          <i>◇</i>
          <div>
            <span>Control plane</span>
            <strong>{data.nodes.filter((node) => node.isControlPlane).length}</strong>
            <small>{data.designatedMasterName ?? 'No master selected'}</small>
          </div>
        </article>
        <article>
          <i>↗</i>
          <div>
            <span>Workers</span>
            <strong>{data.nodes.filter((node) => !node.isControlPlane).length}</strong>
            <small>Available for jobs</small>
          </div>
        </article>
      </section>
      <section className="cluster-fleet">
        <header>
          <div>
            <h2>Fleet nodes</h2>
            <p>Live health, network identity and available compute.</p>
          </div>
          <span>Updated {data.lastSyncLabel}</span>
        </header>
        {data.nodes.length === 0 ? (
          <div className="cluster-empty">
            <span>○</span>
            <strong>No nodes found</strong>
            <small>Add a worker to start building this cluster.</small>
          </div>
        ) : (
          <div className="cluster-node-grid">
            {data.nodes.map((node) => (
              <article
                className={node.ready ? 'cluster-node-card' : 'cluster-node-card warning'}
                key={node.name}
              >
                <header>
                  <div
                    className={
                      node.isControlPlane ? 'cluster-node-avatar master' : 'cluster-node-avatar'
                    }
                  >
                    {node.isControlPlane ? '◇' : '◆'}
                    <span className={node.ready ? 'online' : 'offline'} />
                  </div>
                  <div>
                    <h3>
                      {node.name}
                      {node.isSelf ? <small>this host</small> : null}
                    </h3>
                    <span className={node.ready ? 'ready' : 'not-ready'}>
                      <i />
                      {node.ready ? 'Ready' : 'Not ready'}
                    </span>
                  </div>
                </header>
                <div className="cluster-roles">
                  {node.isDesignatedMaster ? <span className="master">★ Fleet master</span> : null}
                  {node.roles.slice(0, 3).map((role) => (
                    <span key={role}>{role}</span>
                  ))}
                </div>
                <div className="cluster-resources">
                  <div>
                    <span>CPU</span>
                    <strong>{node.cpuCapacity}</strong>
                  </div>
                  <div>
                    <span>Memory</span>
                    <strong>{node.memoryCapacity}</strong>
                  </div>
                </div>
                <dl>
                  <div>
                    <dt>Address</dt>
                    <dd>{node.preferredIp}</dd>
                  </div>
                  <div>
                    <dt>Platform</dt>
                    <dd>{node.platformLabel}</dd>
                  </div>
                  <div>
                    <dt>Kubelet</dt>
                    <dd>{node.kubeletVersion}</dd>
                  </div>
                  <div>
                    <dt>Joined</dt>
                    <dd>{node.relativeAge}</dd>
                  </div>
                </dl>
              </article>
            ))}
          </div>
        )}
      </section>
      {joinCommand === null ? null : (
        <div className="cluster-overlay" onClick={() => void dismissJoin()} role="presentation">
          <section
            aria-modal="true"
            className="cluster-join-dialog"
            onClick={(event) => {
              event.stopPropagation()
            }}
            role="dialog"
          >
            <header>
              <span>＋</span>
              <div>
                <h2>Add a worker</h2>
                <p>Connect another machine to this fleet.</p>
              </div>
              <button aria-label="Close" onClick={() => void dismissJoin()} type="button">
                ×
              </button>
            </header>
            <div className="cluster-join-body">
              <div>
                <span>1</span>
                <p>Open a terminal on the machine you want to add.</p>
              </div>
              <div>
                <span>2</span>
                <p>Run this one-time command:</p>
              </div>
              <pre>
                <code>{joinCommand}</code>
                <button onClick={() => void copyCommand()} type="button">
                  {copied ? '✓ Copied' : 'Copy'}
                </button>
              </pre>
              <aside>
                ⌁ This join token is short-lived. Generate a new command for each worker.
              </aside>
            </div>
          </section>
        </div>
      )}
    </div>
  )
}
