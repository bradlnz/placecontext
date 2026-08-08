interface HostMonaco {
  init(id: string, value: string, language: string, theme: string, path: string): Promise<boolean>
  openFile(id: string, path: string, value: string, language: string): void
  closeFile(id: string, path: string): void
  getValue(id: string): string | null
  destroy(id: string): void
}

declare global {
  interface Window {
    pcmonaco?: HostMonaco
  }
}

let loader: Promise<HostMonaco> | undefined
export function loadHostMonaco(): Promise<HostMonaco> {
  if (window.pcmonaco !== undefined) return Promise.resolve(window.pcmonaco)
  loader ??= new Promise<HostMonaco>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = '/pcmonaco.js?v=4'
    script.async = true
    script.addEventListener(
      'load',
      () => {
        if (window.pcmonaco === undefined) reject(new Error('The code editor did not initialise.'))
        else resolve(window.pcmonaco)
      },
      { once: true },
    )
    script.addEventListener(
      'error',
      () => {
        reject(new Error('The code editor could not be loaded.'))
      },
      { once: true },
    )
    document.head.append(script)
  })
  return loader
}

export function languageForPath(path: string): string {
  const extension = path.slice(path.lastIndexOf('.')).toLocaleLowerCase()
  return (
    (
      {
        '.js': 'javascript',
        '.cjs': 'javascript',
        '.mjs': 'javascript',
        '.ts': 'typescript',
        '.py': 'python',
        '.go': 'go',
        '.rb': 'ruby',
        '.json': 'json',
        '.sh': 'shell',
        '.md': 'markdown',
        '.html': 'html',
        '.css': 'css',
        '.yml': 'yaml',
        '.yaml': 'yaml',
      } as Record<string, string>
    )[extension] ?? 'plaintext'
  )
}
