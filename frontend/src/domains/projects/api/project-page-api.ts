import { getJson, putJson } from '../../../shared/api/http-client'
import type { ProjectPageContext, ProjectRequirements } from '../model/project-page'
import { projectPageContextSchema, projectRequirementsSchema } from './project-page-schemas'

export async function fetchProjectPage(
  projectId: string,
  signal: AbortSignal,
): Promise<ProjectPageContext> {
  return getJson({
    path: `/api/v1/projects/${encodeURIComponent(projectId)}/overview-context`,
    schema: projectPageContextSchema,
    signal,
  })
}

export async function updateProjectRequirements(
  projectId: string,
  markdown: string,
  signal: AbortSignal,
): Promise<ProjectRequirements> {
  return putJson({
    path: `/api/v1/projects/${encodeURIComponent(projectId)}/requirements`,
    body: { markdown },
    schema: projectRequirementsSchema,
    signal,
  })
}
