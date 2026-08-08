export type McpTransport = 'http' | 'sse' | 'stdio'
export type McpAuthType = 'none' | 'bearer' | 'apikey' | 'header' | 'oauth'

export interface McpProject {
  id: string
  name: string
}

export interface McpConnection {
  id: string
  projectId: string
  name: string
  transport: McpTransport
  endpointUrl: string | null
  command: string | null
  args: string | null
  authType: McpAuthType | null
  enabled: boolean
  lastStatus: string | null
  lastConnectedAt: string | null
  createdAt: string
  oAuthTokenExpiresAt: string | null
  oAuthClientId: string | null
  oAuthScopes: string | null
}

export interface McpSettings {
  projectId: string | null
  projects: McpProject[]
  connections: McpConnection[]
}

export interface McpConnectionDraft {
  name: string
  transport: McpTransport
  endpointUrl: string
  command: string
  args: string
  authType: McpAuthType
  authToken: string
  authHeader: string
  oAuthScopes: string
}

export interface CreateMcpConnectionInput {
  name: string
  transport: McpTransport
  endpointUrl: string | null
  command: string | null
  args: string | null
  authType: McpAuthType
  authToken: string | null
  authHeader: string | null
  oAuthScopes: string | null
}
