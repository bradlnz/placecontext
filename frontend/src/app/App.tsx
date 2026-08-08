import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Suspense, useState } from 'react'
import { RouterProvider, type RouterProviderProps } from 'react-router-dom'

import { AppEventBusProvider } from './app-event-bus'
import { appRouter } from './router'
import { SectionLoading } from '../shared/components/loading/SectionLoading'

function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        retry: 1,
        refetchOnWindowFocus: false,
      },
    },
  })
}

interface AppProps {
  queryClient?: QueryClient
  router?: RouterProviderProps['router']
}

export function App({ queryClient, router = appRouter }: AppProps) {
  const [defaultQueryClient] = useState(createQueryClient)
  const resolvedQueryClient = queryClient ?? defaultQueryClient

  return (
    <QueryClientProvider client={resolvedQueryClient}>
      <AppEventBusProvider>
        <Suspense fallback={<SectionLoading />}>
          <RouterProvider router={router} />
        </Suspense>
      </AppEventBusProvider>
    </QueryClientProvider>
  )
}
