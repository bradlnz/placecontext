import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect, useState } from 'react'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider, useAppEventBus } from './app-event-bus'

function EventProbe() {
  const eventBus = useAppEventBus()
  const [projectId, setProjectId] = useState('none')

  useEffect(() => eventBus.subscribe('workspace.project-selected', async (event) => {
    await Promise.resolve()
    setProjectId(event.projectId)
  }), [eventBus])

  async function handlePublish(): Promise<void> {
    await eventBus.publish('workspace.project-selected', { projectId: 'project-7' })
  }

  return (
    <div>
      <output>{projectId}</output>
      <button onClick={() => void handlePublish()} type="button">Publish</button>
    </div>
  )
}

describe('AppEventBusProvider', () => {
  it('provides one async event bus to descendants', async () => {
    const user = userEvent.setup()
    render(
      <AppEventBusProvider>
        <EventProbe />
      </AppEventBusProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'Publish' }))

    expect(screen.getByText('project-7')).toBeVisible()
  })

  it('rejects consumers outside the provider', () => {
    expect(() => render(<EventProbe />)).toThrow(/must be used within AppEventBusProvider/)
  })
})
