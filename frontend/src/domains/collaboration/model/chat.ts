export interface ChatMessage {
  role: string
  content: string
  timestamp: string
}

export interface ChatSession {
  id: string
  projectId: string
  userId: string | null
  title: string | null
  messages: ChatMessage[]
  createdAt: string
  updatedAt: string
}

export interface ChatConfig {
  id: string
  projectId: string
  baseModel: string
  systemPrompt: string
  preamble: string
  toolCatalog: string
  launchpadToolCatalog: string
  maxContextChunks: number
  temperature: number
  topP: number
  enabled: boolean
  createdAt: string
  updatedAt: string
}

export interface ChatPageModel {
  config: ChatConfig
  sessions: ChatSession[]
}

export interface UpdateChatSettingsRequest {
  baseModel: string
  systemPrompt: string
  preamble: string
  toolCatalog: string
  launchpadToolCatalog: string
  maxContextChunks: number
  temperature: number
  topP: number
  enabled: boolean
}
