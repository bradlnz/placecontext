export interface ApiToken {
  id: string
  name: string
  tokenPrefix: string
  createdAt: string
  lastUsedAt: string | null
  expiresAt: string | null
}

export interface CreatedApiToken extends ApiToken {
  rawToken: string
}
