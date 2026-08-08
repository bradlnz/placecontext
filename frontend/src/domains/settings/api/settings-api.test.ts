import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  fetchArtifactFilters,
  fetchBranding,
  fetchLocality,
  fetchMenu,
  resetArtifactFilters,
  resetBranding,
  resetMenu,
  saveArtifactFilters,
  saveBranding,
  saveLocality,
  saveMenu,
} from './settings-api'

const branding = {
  productName: null,
  logoDataUri: null,
  bgColor: null,
  panelColor: null,
  textColor: null,
  accentColor: null,
}
const locality = { timeZoneId: 'UTC', timeZones: ['UTC'] }
const menu = {
  workspace: [
    { id: 'dashboard', defaultLabel: 'Dashboard', label: '', order: 0, visible: true, section: '' },
  ],
}
const artifacts = { categories: [{ id: 'reports', label: 'Reports', prefixes: ['report_'] }] }

describe('settings API', () => {
  afterEach(() => vi.restoreAllMocks())

  it.each([
    [
      'branding GET',
      () => fetchBranding(new AbortController().signal),
      '/api/v1/settings/branding',
      'GET',
      branding,
    ],
    [
      'branding PUT',
      () => saveBranding(branding, new AbortController().signal),
      '/api/v1/settings/branding',
      'PUT',
      branding,
    ],
    [
      'branding reset',
      () => resetBranding(new AbortController().signal),
      '/api/v1/settings/branding/reset',
      'POST',
      branding,
    ],
    [
      'locality GET',
      () => fetchLocality(new AbortController().signal),
      '/api/v1/settings/locality',
      'GET',
      locality,
    ],
    [
      'locality PUT',
      () => saveLocality('UTC', new AbortController().signal),
      '/api/v1/settings/locality',
      'PUT',
      locality,
    ],
    [
      'menu GET',
      () => fetchMenu(new AbortController().signal),
      '/api/v1/settings/menu',
      'GET',
      menu,
    ],
    [
      'menu PUT',
      () => saveMenu(menu.workspace, new AbortController().signal),
      '/api/v1/settings/menu',
      'PUT',
      menu,
    ],
    [
      'menu reset',
      () => resetMenu(new AbortController().signal),
      '/api/v1/settings/menu/reset',
      'POST',
      menu,
    ],
    [
      'artifacts GET',
      () => fetchArtifactFilters(new AbortController().signal),
      '/api/v1/settings/artifacts',
      'GET',
      artifacts,
    ],
    [
      'artifacts PUT',
      () => saveArtifactFilters(artifacts, new AbortController().signal),
      '/api/v1/settings/artifacts',
      'PUT',
      artifacts,
    ],
    [
      'artifacts reset',
      () => resetArtifactFilters(new AbortController().signal),
      '/api/v1/settings/artifacts/reset',
      'POST',
      artifacts,
    ],
  ])('uses canonical api/v1 for %s', async (_name, request, path, method, responseBody) => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(responseBody), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    await expect(request()).resolves.toEqual(responseBody)
    expect(fetchMock).toHaveBeenCalledWith(path, expect.objectContaining({ method }))
  })
})
