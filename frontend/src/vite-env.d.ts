/// <reference types="vite/client" />

interface Window {
  pcchart?: {
    render: (id: string, spec: Record<string, unknown>) => void
    destroy: (id: string) => void
  }
}
