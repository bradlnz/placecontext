const LOCAL_STORAGE_KEY = 'placecontext:selected-project:v1'

export async function navigateToLegacyProject(projectId: string): Promise<void> {
  await saveSelectedProject(projectId)
  window.location.assign(`/project/${encodeURIComponent(projectId)}`)
}

export async function navigateToLegacyPath(path: string): Promise<void> {
  await Promise.resolve()
  if (!path.startsWith('/') || path.startsWith('//')) {
    throw new Error('Legacy navigation requires a local absolute path.')
  }

  window.location.assign(path)
}

async function saveSelectedProject(projectId: string): Promise<void> {
  await Promise.resolve()

  try {
    window.localStorage.setItem(LOCAL_STORAGE_KEY, projectId)
  } catch {
    // Storage can be unavailable in private browsing. Navigation must still succeed.
  }
}
