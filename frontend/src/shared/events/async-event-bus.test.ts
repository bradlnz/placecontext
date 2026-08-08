import { describe, expect, it, vi } from 'vitest'

import { AsyncEventBus } from './async-event-bus'

interface TestEvents {
  completed: { id: string }
}

describe('AsyncEventBus', () => {
  it('awaits every subscriber before resolving', async () => {
    const bus = new AsyncEventBus<TestEvents>()
    const calls: string[] = []
    const first = vi.fn(async ({ id }: { id: string }) => {
      await Promise.resolve()
      calls.push(`first:${id}`)
    })
    const second = vi.fn(async ({ id }: { id: string }) => {
      await Promise.resolve()
      calls.push(`second:${id}`)
    })

    bus.subscribe('completed', first)
    bus.subscribe('completed', second)
    await bus.publish('completed', { id: '42' })

    expect(calls).toEqual(['first:42', 'second:42'])
  })

  it('removes a subscriber with its cleanup callback', async () => {
    const bus = new AsyncEventBus<TestEvents>()
    const subscriber = vi.fn(async () => Promise.resolve())
    const unsubscribe = bus.subscribe('completed', subscriber)

    unsubscribe()
    await bus.publish('completed', { id: '42' })

    expect(subscriber).not.toHaveBeenCalled()
  })

  it('reports subscriber failures after all handlers settle', async () => {
    const bus = new AsyncEventBus<TestEvents>()
    const successfulSubscriber = vi.fn(async () => Promise.resolve())
    bus.subscribe('completed', async () => {
      await Promise.resolve()
      throw new Error('subscriber failed')
    })
    bus.subscribe('completed', successfulSubscriber)

    await expect(bus.publish('completed', { id: '42' })).rejects.toThrow(AggregateError)
    expect(successfulSubscriber).toHaveBeenCalledOnce()
  })
})
