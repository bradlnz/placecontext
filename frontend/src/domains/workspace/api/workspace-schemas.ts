import { z } from 'zod'

export const workspaceProjectSchema = z.object({
  id: z.uuid(),
  name: z.string(),
  path: z.string(),
  status: z.string(),
  isGraphified: z.boolean(),
  godNodeCount: z.number().int().nonnegative(),
  nodeCount: z.number().int().nonnegative(),
  linkCount: z.number().int().nonnegative(),
})

export const workspaceProjectsSchema = z.array(workspaceProjectSchema)

export const workspaceFocusSchema = z.object({
  items: z.array(
    z.object({
      kind: z.string(),
      severity: z.string(),
      title: z.string(),
      detail: z.string(),
      projectId: z.uuid(),
      project: z.string(),
      url: z.string(),
    }),
  ),
  projectCount: z.number().int().nonnegative(),
})

export const workspaceStatsSchema = z.object({
  projectCount: z.number().int().nonnegative(),
  changesToday: z.number().int().nonnegative(),
  agentChangesToday: z.number().int().nonnegative(),
  humanChangesToday: z.number().int().nonnegative(),
  godNodeTotal: z.number().int().nonnegative(),
  staleContextCount: z.number().int().nonnegative(),
})

export const workspaceSessionSchema = z.object({
  displayName: z.string(),
  role: z.string(),
  tenant: z.string(),
})
