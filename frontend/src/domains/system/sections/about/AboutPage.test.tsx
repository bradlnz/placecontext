import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { AboutPage } from './AboutPage'

describe('AboutPage', () => {
  it('replicates the Host product, ownership, and attribution content', () => {
    render(<AboutPage />)

    expect(screen.getByText('PlaceContext')).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Platform' })).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Your data & jobs' })).toBeVisible()
    expect(screen.getByText(/THIRD-PARTY-NOTICES\.md/)).toBeVisible()
    expect(screen.getByText(/Created by/)).toBeVisible()
  })
})
