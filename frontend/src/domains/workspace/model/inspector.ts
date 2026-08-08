export interface InspectorToolCall {
  id: string
  tool: string
  direction: string
  project: string
  summary: string
  status: string
  durationMs: number
  requestJson: string
  responseJson: string
  at: string
}
