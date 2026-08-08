import { useSuspenseQuery } from '@tanstack/react-query'
import { useState, type MouseEvent } from 'react'
import { NavLink, useNavigate, useParams } from 'react-router-dom'

import { wikiContextQueryOptions } from '../../api/wiki-query'

export function WikiPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()
  const { data } = useSuspenseQuery(wikiContextQueryOptions(slug))
  const [contentsOpen, setContentsOpen] = useState(false)

  async function toggleContents(): Promise<void> {
    await Promise.resolve()
    setContentsOpen((current) => !current)
  }

  async function closeContents(): Promise<void> {
    await Promise.resolve()
    setContentsOpen(false)
  }

  function handleArticleClick(event: MouseEvent<HTMLElement>): void {
    if (!(event.target instanceof Element)) return
    const link = event.target.closest('a')
    const href = link?.getAttribute('href')
    if (!href?.startsWith('/wiki')) return
    event.preventDefault()
    void navigate(href)
  }

  return (
    <div className="wiki-page">
      <title>PlaceContext — Wiki: {data.article?.title ?? 'Docs'}</title>
      <button
        aria-controls="wiki-contents"
        aria-expanded={contentsOpen}
        className="wiki-toc-toggle"
        onClick={() => void toggleContents()}
        type="button"
      >
        <span>Documentation</span>
        <span>{data.article?.title ?? 'Choose an article'}</span>
        <span aria-hidden="true">{contentsOpen ? '−' : '+'}</span>
      </button>
      <nav
        aria-label="Documentation articles"
        className={contentsOpen ? 'wiki-toc open' : 'wiki-toc'}
        id="wiki-contents"
      >
        <div className="wiki-toc-head">Documentation</div>
        {data.articles.map((article) => (
          <NavLink
            className={({ isActive }) => (isActive ? 'wiki-toc-link active' : 'wiki-toc-link')}
            key={article.slug}
            onClick={() => void closeContents()}
            to={`/wiki/${article.slug}`}
          >
            {article.title}
          </NavLink>
        ))}
      </nav>
      <article className="wiki-article">
        {data.article === null ? (
          <div className="wiki-not-found">
            Article not found. <NavLink to="/wiki">Back to the docs home →</NavLink>
          </div>
        ) : (
          <>
            <div className="wiki-summary">{data.article.summary}</div>
            <div
              className="wiki-content"
              dangerouslySetInnerHTML={{ __html: data.article.html }}
              onClick={handleArticleClick}
            />
          </>
        )}
      </article>
    </div>
  )
}
