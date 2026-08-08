import { getJson } from '../../../shared/api/http-client'
import type { ProjectGraph } from '../model/project-graph'
import { projectGraphSchema } from './project-graph-schemas'

export async function fetchProjectGraph(
  projectId: string,
  signal: AbortSignal,
): Promise<ProjectGraph> {
  return getJson({
    path: `/api/v1/projects/${encodeURIComponent(projectId)}/data-graph`,
    schema: projectGraphSchema,
    signal,
  })
}
