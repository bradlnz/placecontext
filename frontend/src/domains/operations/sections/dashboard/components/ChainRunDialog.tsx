import { useState } from 'react'

import { useAppEventBus } from '../../../../../app/app-event-bus'
import type { DashboardChain, DashboardParameter } from '../../../model/dashboard'

interface ChainRunDialogProps {
  chain: DashboardChain
  running: boolean
  onClose: () => void
}

function fieldKey(stepIndex: number, parameterName: string): string {
  return `${String(stepIndex)}:${parameterName}`
}

function initialValues(chain: DashboardChain): Record<string, string> {
  return Object.fromEntries(
    chain.promptSteps.flatMap((step) =>
      step.parameters.map((parameter) => [
        fieldKey(step.index, parameter.name),
        parameter.defaultValue,
      ]),
    ),
  )
}

function parameterField({
  parameter,
  value,
  onChange,
}: {
  parameter: DashboardParameter
  value: string
  onChange: (value: string) => Promise<void>
}) {
  if (parameter.type === 'select') {
    return (
      <select value={value} onChange={(event) => void onChange(event.target.value)}>
        <option value="">Select…</option>
        {parameter.options.map((option) => (
          <option key={option}>{option}</option>
        ))}
      </select>
    )
  }

  if (parameter.type === 'checkbox') {
    return (
      <input
        checked={value === 'true'}
        onChange={(event) => void onChange(String(event.target.checked))}
        type="checkbox"
      />
    )
  }

  return (
    <input
      inputMode={parameter.type === 'number' ? 'decimal' : undefined}
      onChange={(event) => void onChange(event.target.value)}
      placeholder={parameter.type === 'file' ? 'Artifact reference' : undefined}
      type={parameter.type === 'number' ? 'number' : 'text'}
      value={value}
    />
  )
}

export function ChainRunDialog({ chain, running, onClose }: ChainRunDialogProps) {
  const eventBus = useAppEventBus()
  const [values, setValues] = useState(() => initialValues(chain))
  const [error, setError] = useState<string | null>(null)

  async function handleValueChanged(key: string, value: string): Promise<void> {
    await Promise.resolve()
    setValues((current) => ({ ...current, [key]: value }))
  }

  async function handleSubmit(): Promise<void> {
    const missing = chain.promptSteps.flatMap((step) =>
      step.parameters
        .filter(
          (parameter) =>
            parameter.required &&
            (values[fieldKey(step.index, parameter.name)] ?? '').trim().length === 0,
        )
        .map((parameter) => `step ${String(step.index + 1)}: ${parameter.label}`),
    )
    if (missing.length > 0) {
      setError(`Required: ${missing.join(', ')}`)
      return
    }

    const stepPayloadOverrides = Object.fromEntries(
      chain.promptSteps.map((step) => [
        step.index,
        JSON.stringify(
          Object.fromEntries(
            step.parameters.map((parameter) => [
              parameter.name,
              values[fieldKey(step.index, parameter.name)] ?? '',
            ]),
          ),
        ),
      ]),
    )

    await eventBus.publish('dashboard.chain-run-requested', {
      projectId: chain.projectId,
      chainId: chain.id,
      inputPayload: stepPayloadOverrides[0] ?? null,
      stepPayloadOverrides,
    })
    onClose()
  }

  return (
    <div className="dcmodal-overlay" role="presentation">
      <div
        aria-labelledby="chain-run-title"
        aria-modal="true"
        className="dcmodal chain-run-modal"
        role="dialog"
      >
        <div className="dcmodal-head">
          <div>
            <div className="title-14" id="chain-run-title">
              Run {chain.name}
            </div>
            <div className="chain-run-modal-sub">
              Enter the declared inputs for each chain step. Stored job values are prefilled.
            </div>
          </div>
          <button
            aria-label="Close run input prompt"
            disabled={running}
            onClick={onClose}
            type="button"
          >
            ×
          </button>
        </div>
        <div className="dcmodal-body">
          {chain.promptSteps.map((step) => (
            <section className="chain-prompt-step" key={step.index}>
              <div className="chain-prompt-head">
                <span>step {step.index + 1}</span>
                <strong>{step.jobName}</strong>
              </div>
              {step.parameters.map((parameter) => {
                const key = fieldKey(step.index, parameter.name)
                return (
                  <label className="dcfield" key={key}>
                    <span>
                      {parameter.label}
                      {parameter.required ? <span className="required"> *</span> : null}{' '}
                      <code>{parameter.name}</code>
                    </span>
                    {parameterField({
                      parameter,
                      value: values[key] ?? '',
                      onChange: async (value) => handleValueChanged(key, value),
                    })}
                  </label>
                )
              })}
            </section>
          ))}
          {error === null ? null : (
            <div className="chain-prompt-error" role="alert">
              {error}
            </div>
          )}
        </div>
        <div className="dcmodal-foot">
          <button className="dcbtn" disabled={running} onClick={onClose} type="button">
            Cancel
          </button>
          <button
            className="dcbtn primary"
            disabled={running}
            onClick={() => void handleSubmit()}
            type="button"
          >
            {running ? 'Starting…' : '▶ Run chain'}
          </button>
        </div>
      </div>
    </div>
  )
}
