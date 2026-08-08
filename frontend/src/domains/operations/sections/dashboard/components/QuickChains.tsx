import { useState } from 'react'

import { useAppEventBus } from '../../../../../app/app-event-bus'
import type { DashboardChain, DashboardProject } from '../../../model/dashboard'
import { ChainRunDialog } from './ChainRunDialog'

interface QuickChainsProps {
  project: DashboardProject | null
  chains: DashboardChain[]
  runningChainId: string | null
  message: string | null
  hasError: boolean
}

export function QuickChains({ project, chains, runningChainId, message, hasError }: QuickChainsProps) {
  const eventBus = useAppEventBus()
  const [promptChain, setPromptChain] = useState<DashboardChain | null>(null)

  async function handleRun(chain: DashboardChain): Promise<void> {
    if (chain.promptSteps.length > 0) {
      setPromptChain(chain)
      return
    }

    await eventBus.publish('dashboard.chain-run-requested', {
      projectId: chain.projectId,
      chainId: chain.id,
      inputPayload: null,
      stepPayloadOverrides: null,
    })
  }

  return (
    <>
      <section aria-labelledby="quick-chain-title" className="dccard dashboard-quick-chains">
        <div className="quick-chain-head">
          <div>
            <div className="quick-chain-kicker">QUICK ACTION</div>
            <h2 className="quick-chain-title" id="quick-chain-title">Run a job chain</h2>
            <p className="quick-chain-sub">Start a repeatable workflow from the current project.</p>
          </div>
          {project === null ? null : <a className="dcbtn" href={`/project/${project.id}/chains`}>View chains</a>}
        </div>

        {project === null ? <div className="quick-chain-empty">Select a project to run its job chains.</div> : null}
        {project !== null && chains.length === 0 ? <div className="quick-chain-empty">No job chains are configured for this project.</div> : null}
        {chains.length > 0 ? (
          <div className="quick-chain-list">
            {chains.slice(0, 4).map((chain) => (
              <div className="quick-chain-row" key={chain.id}>
                <div className="quick-chain-copy">
                  <div className="quick-chain-name">{chain.name}</div>
                  <div className="quick-chain-meta">{chain.stageCount} {chain.stageCount === 1 ? 'stage' : 'stages'} · {chain.jobCount} {chain.jobCount === 1 ? 'job' : 'jobs'}</div>
                </div>
                <button
                  aria-label={`Run ${chain.name}`}
                  className="dcbtn primary quick-chain-run"
                  disabled={runningChainId !== null}
                  onClick={() => void handleRun(chain)}
                  type="button"
                >
                  {runningChainId === chain.id ? 'Starting…' : 'Run'}
                </button>
              </div>
            ))}
          </div>
        ) : null}
        {message === null ? null : <div className={`quick-chain-message${hasError ? ' error' : ''}`} role="status">{message}</div>}
      </section>

      {promptChain === null ? null : (
        <ChainRunDialog
          chain={promptChain}
          onClose={() => {
            setPromptChain(null)
          }}
          running={runningChainId === promptChain.id}
        />
      )}
    </>
  )
}
