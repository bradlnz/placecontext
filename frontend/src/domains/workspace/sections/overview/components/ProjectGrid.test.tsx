import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { AppEventBusProvider } from '../../../../../app/app-event-bus'
import { AsyncEventBus } from '../../../../../shared/events/async-event-bus'
import { workspaceProjectFixture } from '../../../../../test/fixtures/workspace'
import { ProjectGrid } from './ProjectGrid'

describe('ProjectGrid', () => {
  it('publishes the selected project from a project card', async () => {
    const user = userEvent.setup()
    const publish = vi.spyOn(AsyncEventBus.prototype, 'publish').mockResolvedValue()
    render(
      <AppEventBusProvider>
        <ProjectGrid projects={[workspaceProjectFixture]} />
      </AppEventBusProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'Open Atlas' }))

    expect(publish).toHaveBeenCalledWith('workspace.project-selected', {
      projectId: workspaceProjectFixture.id,
    })
    expect(screen.getByText('1,420 nodes')).toBeVisible()
    expect(screen.getByText('3,098 links')).toBeVisible()
    expect(screen.getByText('graphified')).toHaveClass('foot-graphified')
  })

  it('renders migration guidance for an empty workspace', () => {
    render(
      <AppEventBusProvider>
        <ProjectGrid projects={[]} />
      </AppEventBusProvider>,
    )

    expect(screen.getByText(/No projects yet/)).toBeVisible()
    expect(screen.getByText('create_project')).toBeVisible()
  })
})
