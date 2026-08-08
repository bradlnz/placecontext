export type AsyncEventHandler<TPayload> = (payload: TPayload) => Promise<void>

export class AsyncEventBus<TEvents extends Record<keyof TEvents, unknown>> {
  private readonly handlers = new Map<
    keyof TEvents,
    Set<AsyncEventHandler<TEvents[keyof TEvents]>>
  >()

  subscribe<TKey extends keyof TEvents>(
    eventName: TKey,
    handler: AsyncEventHandler<TEvents[TKey]>,
  ): () => void {
    const existingHandlers = this.handlers.get(eventName) as
      | Set<AsyncEventHandler<TEvents[TKey]>>
      | undefined
    const eventHandlers = existingHandlers ?? new Set<AsyncEventHandler<TEvents[TKey]>>()

    eventHandlers.add(handler)
    this.handlers.set(
      eventName,
      eventHandlers as Set<AsyncEventHandler<TEvents[keyof TEvents]>>,
    )

    return () => {
      eventHandlers.delete(handler)

      if (eventHandlers.size === 0) {
        this.handlers.delete(eventName)
      }
    }
  }

  async publish<TKey extends keyof TEvents>(
    eventName: TKey,
    payload: TEvents[TKey],
  ): Promise<void> {
    const eventHandlers = this.handlers.get(eventName) as
      | Set<AsyncEventHandler<TEvents[TKey]>>
      | undefined

    if (eventHandlers === undefined) {
      return
    }

    const outcomes = await Promise.allSettled(
      [...eventHandlers].map(async (handler) => handler(payload)),
    )
    const failures: unknown[] = []
    for (const outcome of outcomes) {
      if (outcome.status === 'rejected') {
        const failure: unknown = outcome.reason
        failures.push(failure)
      }
    }

    if (failures.length > 0) {
      throw new AggregateError(failures, `Event "${String(eventName)}" failed.`)
    }
  }
}
