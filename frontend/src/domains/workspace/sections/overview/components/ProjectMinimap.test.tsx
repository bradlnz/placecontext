import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { workspaceProjectFixture } from '../../../../../test/fixtures/workspace'
import { ProjectMinimap } from './ProjectMinimap'

describe('ProjectMinimap', () => {
  it('renders a deterministic bounded graph with important nodes', () => {
    const { container, rerender } = render(<ProjectMinimap project={workspaceProjectFixture} />)
    const firstGraph = container.querySelector('svg')?.innerHTML

    expect(container.querySelectorAll('.minimap-node')).toHaveLength(16)
    expect(container.querySelectorAll('.minimap-node--important')).toHaveLength(2)

    rerender(<ProjectMinimap project={workspaceProjectFixture} />)
    expect(container.querySelector('svg')?.innerHTML).toBe(firstGraph)
  })

  it('caps graph density for projects with many god nodes', () => {
    const { container } = render(
      <ProjectMinimap project={{ ...workspaceProjectFixture, godNodeCount: 100 }} />,
    )

    expect(container.querySelectorAll('.minimap-node')).toHaveLength(22)
  })
})
