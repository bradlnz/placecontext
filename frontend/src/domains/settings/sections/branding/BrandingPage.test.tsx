import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { brandingQueryOptions } from '../../api/settings-queries'
import { BrandingPage } from './BrandingPage'

describe('BrandingPage', () => {
  it('replicates the Host branding fields and previews a logo asynchronously', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(brandingQueryOptions.queryKey, {
      productName: 'Acme Context',
      logoDataUri: null,
      bgColor: null,
      panelColor: null,
      textColor: null,
      accentColor: null,
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <BrandingPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )

    expect(screen.getByRole('heading', { name: 'Whitelabel branding' })).toBeVisible()
    expect(screen.getByDisplayValue('Acme Context')).toBeVisible()
    await user.upload(
      screen.getByLabelText(/Logo/),
      new File(['logo'], 'logo.png', { type: 'image/png' }),
    )
    expect(await screen.findByRole('img', { name: 'logo' })).toHaveAttribute(
      'src',
      expect.stringMatching(/^data:image\/png;base64,/),
    )
    expect(screen.getByRole('button', { name: 'Save branding' })).toBeEnabled()
  })
})
