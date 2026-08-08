import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { AuthBrand } from './AuthBrand'

describe('AuthBrand', () => {
  it('renders the compact PlaceContext identity', () => {
    const { container } = render(<AuthBrand />)

    expect(screen.getByText('placecontext')).toBeVisible()
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })
})
