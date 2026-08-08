import { getJson } from '../../../shared/api/http-client'
import type { InspectorToolCall } from '../model/inspector'
import { inspectorToolCallsSchema } from './inspector-schemas'

export async function fetchInspectorToolCalls(signal: AbortSignal): Promise<InspectorToolCall[]> {
  return getJson({
    path: '/api/v1/inspector/tool-calls?take=20',
    schema: inspectorToolCallsSchema,
    signal,
  })
}
