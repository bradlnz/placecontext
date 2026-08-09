import { getJson, postJson } from '../../../shared/api/http-client'
import type { ObservabilityPageModel, ObservabilityRunDetails } from '../model/observability'
import {
  observabilityPageSchema,
  observabilityJobRunDetailsSchema,
  observabilityRunDetailsSchema,
  observabilityRunArtifactsSchema,
  replayObservabilityRunSchema,
} from './observability-schemas'

const ROOT = '/api/jobs/observability'

export const fetchObservabilityPage = (signal: AbortSignal): Promise<ObservabilityPageModel> =>
  getJson({ path: ROOT, schema: observabilityPageSchema, signal })

export const fetchObservabilityRunDetails = (
  runId: string,
  jobId: string,
  signal: AbortSignal,
): Promise<ObservabilityRunDetails> =>
  Promise.all([
    getJson({
      path: `${ROOT}/runs/${encodeURIComponent(runId)}?jobId=${encodeURIComponent(jobId)}`,
      schema: observabilityJobRunDetailsSchema,
      signal,
    }),
    getJson({
      path: `/api/artifacts/runs/${encodeURIComponent(runId)}`,
      schema: observabilityRunArtifactsSchema,
      signal,
    }),
  ]).then(([details, artifacts]) =>
    observabilityRunDetailsSchema.parse({ ...details, artifacts }),
  )

export const replayObservabilityRun = (
  runId: string,
  signal: AbortSignal,
): Promise<{ runId: string; status: string }> =>
  postJson({
    path: `${ROOT}/runs/${encodeURIComponent(runId)}/replay`,
    body: {},
    schema: replayObservabilityRunSchema,
    signal,
  })
