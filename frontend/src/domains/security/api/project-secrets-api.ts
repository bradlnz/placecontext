import { deleteRequest, getJson, postJson } from '../../../shared/api/http-client'
import type { CreateProjectSecretCommand, ProjectSecret } from '../model/project-secret'
import { projectSecretSchema, projectSecretsSchema } from './project-secrets-schemas'

function projectSecretsPath(projectId: string): string {
  return `/api/v1/projects/${encodeURIComponent(projectId)}/secrets`
}

export async function fetchProjectSecrets(
  projectId: string,
  signal: AbortSignal,
): Promise<ProjectSecret[]> {
  return getJson({
    path: projectSecretsPath(projectId),
    schema: projectSecretsSchema,
    signal,
  })
}

export async function createProjectSecret(
  projectId: string,
  command: CreateProjectSecretCommand,
  signal: AbortSignal,
): Promise<ProjectSecret> {
  return postJson({
    path: projectSecretsPath(projectId),
    body: command,
    schema: projectSecretSchema,
    signal,
  })
}

export async function deleteProjectSecret(
  projectId: string,
  name: string,
  signal: AbortSignal,
): Promise<void> {
  return deleteRequest(`${projectSecretsPath(projectId)}/${encodeURIComponent(name)}`, signal)
}
