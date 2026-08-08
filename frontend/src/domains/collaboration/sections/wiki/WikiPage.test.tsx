import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { wikiContextQueryOptions } from '../../api/wiki-query'
import { WikiPage } from './WikiPage'

const article = {
  slug: 'getting-started',
  title: 'Getting started',
  summary: 'Start here.',
  html: '<h1>Getting started</h1><p><a href="/wiki/projects">Projects</a></p>',
}

function renderWiki(path: string, currentArticle: typeof article | null = article) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const slug = path.split('/')[2]
  queryClient.setQueryData(wikiContextQueryOptions(slug).queryKey, {
    articles: [{ slug: article.slug, title: article.title, summary: article.summary }],
    article: currentArticle,
  })
  const router = createMemoryRouter([{ path: '/wiki/:slug?', element: <WikiPage /> }], {
    initialEntries: [path],
  })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

describe('WikiPage', () => {
  it('renders the Host-produced article HTML and keeps wiki links in React', async () => {
    const user = userEvent.setup()
    const router = renderWiki('/wiki/getting-started')

    expect(screen.getByRole('heading', { name: 'Getting started' })).toBeVisible()
    await user.click(screen.getByRole('link', { name: 'Projects' }))
    expect(router.state.location.pathname).toBe('/wiki/projects')
  })

  it('shows the canonical not-found state', () => {
    renderWiki('/wiki/missing', null)
    expect(screen.getByText(/Article not found/)).toBeVisible()
  })
})
