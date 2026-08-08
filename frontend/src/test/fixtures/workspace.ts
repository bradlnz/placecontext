import type { WorkspaceOverview, WorkspaceProject } from '../../domains/workspace/model/workspace'

export const workspaceProjectFixture: WorkspaceProject = {
  id: 'd809bf2d-45b8-4ac0-828e-b0bb10ad296d',
  name: 'Atlas',
  path: '/code/atlas',
  status: 'Ready',
  isGraphified: true,
  godNodeCount: 2,
  nodeCount: 1420,
  linkCount: 3098,
}

export const workspaceOverviewFixture: WorkspaceOverview = {
  projects: [workspaceProjectFixture],
  focus: {
    projectCount: 1,
    items: [
      {
        kind: 'stale-context',
        severity: 'high',
        title: 'Re-index Atlas',
        detail: 'Context is behind the working tree.',
        projectId: workspaceProjectFixture.id,
        project: workspaceProjectFixture.name,
        url: `/project/${workspaceProjectFixture.id}`,
      },
    ],
  },
  stats: {
    projectCount: 1,
    changesToday: 8,
    agentChangesToday: 5,
    humanChangesToday: 3,
    godNodeTotal: 2,
    staleContextCount: 1,
  },
  session: {
    displayName: 'Ada Lovelace',
    role: 'Owner',
    tenant: 'analytical-engine',
  },
}
