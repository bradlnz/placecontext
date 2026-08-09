import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { workspaceOverviewFixture } from '../../../../test/fixtures/workspace'
import { workspaceQueryKeys } from '../../../workspace/api/workspace-query-options'
import { chatQueryKeys } from '../../api/chat-query'
import type { ChatPageModel } from '../../model/chat'
import { ChatPage } from './ChatPage'

const api = vi.hoisted(() => ({
  fetchChatPage: vi.fn(),
  sendChatMessage: vi.fn(),
  updateChatSettings: vi.fn(),
}))

vi.mock('../../api/chat-api', () => api)

const project = firstWorkspaceProject()

const initialPage: ChatPageModel = {
  config: {
    id: 'e8582ae5-11a7-4459-8613-09db7a993c93',
    projectId: project.id,
    baseModel: 'qwen3.5:0.8b',
    systemPrompt: 'Answer from project context.',
    preamble: '',
    toolCatalog: '',
    launchpadToolCatalog: '',
    maxContextChunks: 5,
    temperature: 0.7,
    topP: 0.9,
    enabled: true,
    createdAt: '2026-08-09T01:00:00+00:00',
    updatedAt: '2026-08-09T01:00:00+00:00',
  },
  sessions: [],
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  queryClient.setQueryData(workspaceQueryKeys.projects, workspaceOverviewFixture.projects)
  queryClient.setQueryData(chatQueryKeys.page(project.id), initialPage)

  render(
    <QueryClientProvider client={queryClient}>
      <ChatPage />
    </QueryClientProvider>,
  )
}

describe('ChatPage', () => {
  beforeEach(() => {
    api.sendChatMessage.mockReset()
    api.updateChatSettings.mockReset()
  })

  it('starts a persisted project conversation from a starter prompt', async () => {
    const user = userEvent.setup()
    api.sendChatMessage.mockResolvedValue({
      id: 'b6bb4d30-e28e-4835-bc65-d67635e2f7d4',
      projectId: project.id,
      userId: null,
      title: 'Give me an overview of this project',
      messages: [
        {
          role: 'user',
          content: 'Give me an overview of this project',
          timestamp: '2026-08-09T01:01:00+00:00',
        },
        {
          role: 'assistant',
          content: 'This project has three active jobs.',
          timestamp: '2026-08-09T01:01:01+00:00',
        },
      ],
      createdAt: '2026-08-09T01:01:00+00:00',
      updatedAt: '2026-08-09T01:01:01+00:00',
    })
    renderPage()

    await user.click(screen.getByRole('button', { name: 'Give me an overview of this project' }))

    expect(await screen.findByText('This project has three active jobs.')).toBeVisible()
    expect(api.sendChatMessage).toHaveBeenCalledWith(
      project.id,
      null,
      'Give me an overview of this project',
      expect.any(AbortSignal),
    )
    expect(screen.getByRole('button', { name: /Give me an overview/ })).toHaveAttribute(
      'aria-current',
      'true',
    )
  })

  it('updates project agent settings without discarding hidden configuration', async () => {
    const user = userEvent.setup()
    api.updateChatSettings.mockResolvedValue(initialPage.config)
    renderPage()

    await user.click(screen.getByRole('button', { name: /Settings/ }))
    const prompt = screen.getByRole('textbox', { name: 'System prompt' })
    await user.clear(prompt)
    await user.type(prompt, 'Keep answers concise.')
    await user.click(screen.getByRole('button', { name: 'Save settings' }))

    expect(api.updateChatSettings).toHaveBeenCalledWith(
      project.id,
      expect.objectContaining({
        systemPrompt: 'Keep answers concise.',
        toolCatalog: initialPage.config.toolCatalog,
        topP: initialPage.config.topP,
      }),
      expect.any(AbortSignal),
    )
  })
})

function firstWorkspaceProject() {
  const first = workspaceOverviewFixture.projects[0]
  if (first === undefined) throw new Error('Workspace fixture needs a project.')
  return first
}
