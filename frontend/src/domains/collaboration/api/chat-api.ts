import { getJson, postJson, putJson } from '../../../shared/api/http-client'
import type {
  ChatConfig,
  ChatPageModel,
  ChatSession,
  UpdateChatSettingsRequest,
} from '../model/chat'
import { chatConfigSchema, chatPageSchema, chatSessionSchema } from './chat-schemas'

const root = (projectId: string) => `/api/agent-chat/projects/${encodeURIComponent(projectId)}`

export const fetchChatPage = (projectId: string, signal: AbortSignal): Promise<ChatPageModel> =>
  getJson({ path: `${root(projectId)}/page`, schema: chatPageSchema, signal })

export const sendChatMessage = (
  projectId: string,
  sessionId: string | null,
  message: string,
  signal: AbortSignal,
): Promise<ChatSession> =>
  postJson({
    path: `${root(projectId)}/messages`,
    body: { sessionId, message },
    schema: chatSessionSchema,
    signal,
  })

export const updateChatSettings = (
  projectId: string,
  settings: UpdateChatSettingsRequest,
  signal: AbortSignal,
): Promise<ChatConfig> =>
  putJson({
    path: `${root(projectId)}/settings`,
    body: settings,
    schema: chatConfigSchema,
    signal,
  })
