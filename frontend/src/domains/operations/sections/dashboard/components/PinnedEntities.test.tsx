import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import { PinnedEntities } from './PinnedEntities'

describe('PinnedEntities', () => {
  it('renders entity counts and distribution bars', () => {
    render(<PinnedEntities entities={dashboardFixture.entities} />)

    expect(screen.getByRole('link', { name: /customers/i })).toHaveAttribute(
      'href',
      `/project/${dashboardFixture.project?.id ?? ''}/entity/Customers`,
    )
    expect(screen.getByText('1,280')).toBeVisible()
    expect(screen.getByText('Queensland')).toBeVisible()
  })

  it('renders nothing for an empty entity collection', () => {
    const { container } = render(<PinnedEntities entities={[]} />)
    expect(container).toBeEmptyDOMElement()
  })
})
