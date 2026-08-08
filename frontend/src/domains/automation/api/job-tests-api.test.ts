import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createJobTest,
  deleteJobTest,
  fetchJobTestCode,
  fetchJobTests,
  runJobTest,
  runJobTestCode,
  saveJobTestCode,
  updateJobTest,
} from './job-tests-api'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const testId = '158fdb23-5c46-4777-b0bb-d78ff91b8754'
const jobId = '79d2d944-56ef-4597-a64d-10b56c18e33d'
const block = {
  id: testId,
  projectId,
  jobId,
  jobName: 'Import',
  name: 'contract',
  inputPayload: '{}',
  assertionType: 'Succeeds',
  expectedValue: null,
  enabled: true,
  lastStatus: 'NotRun',
  lastMessage: null,
  lastActualOutput: null,
  lastDurationMs: null,
  runtimeId: 'python',
  runtimeLabel: 'pytest',
  entrypoint: 'test_job.py',
  codeFiles: [{ path: 'test_job.py', content: 'def test_ok(): pass' }],
  methodResults: [{ name: 'test_ok', status: 'NotRun', durationMs: null, message: null }],
}
const codePage = {
  test: block,
  runtimes: [
    {
      id: 'python',
      label: 'Python',
      frameworkLabel: 'pytest',
      entrypoint: 'test_job.py',
      starterFiles: block.codeFiles,
    },
  ],
}
const requestUrl = (input: RequestInfo | URL): string =>
  input instanceof Request ? input.url : input instanceof URL ? input.href : input

describe('Job tests API', () => {
  afterEach(() => vi.restoreAllMocks())
  it('covers list, block CRUD/run, and code save/run routes', async () => {
    const mock = vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (init?.method === 'DELETE') return Promise.resolve(new Response(null, { status: 204 }))
      const url = requestUrl(input)
      const body =
        init?.method === 'GET'
          ? url.endsWith('/code-page')
            ? codePage
            : { jobs: [{ id: jobId, name: 'Import' }], tests: [block] }
          : block
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }))
    })
    const signal = new AbortController().signal
    const edit = {
      jobId,
      name: 'contract',
      inputPayload: '{}',
      assertionType: 'Succeeds' as const,
      expectedValue: '',
      enabled: true,
    }
    const code = {
      runtimeId: 'python',
      entrypoint: 'test_job.py',
      codeFiles: block.codeFiles,
    }
    await fetchJobTests(projectId, signal)
    await createJobTest(projectId, edit, signal)
    await updateJobTest(projectId, testId, edit, signal)
    await runJobTest(projectId, testId, signal)
    await deleteJobTest(projectId, testId, signal)
    await fetchJobTestCode(projectId, testId, signal)
    await saveJobTestCode(projectId, testId, code, signal)
    await runJobTestCode(projectId, testId, code, signal)
    expect(mock).toHaveBeenCalledTimes(8)
    expect(mock.mock.calls.map(([input]) => requestUrl(input))).toContain(
      `/api/v1/projects/${projectId}/test-page/tests/${testId}/code-page/run`,
    )
  })
})
