export interface WikiArticleSummary {
  slug: string
  title: string
  summary: string
}

export interface WikiArticle extends WikiArticleSummary {
  html: string
}

export interface WikiContext {
  articles: WikiArticleSummary[]
  article: WikiArticle | null
}
