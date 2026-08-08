import { z } from 'zod'

export const clusterPageSchema = z.object({
  isRealCluster: z.boolean(),
  designatedMasterName: z.string().nullable(),
  nodes: z.array(
    z.object({
      name: z.string(),
      roles: z.array(z.string()),
      ready: z.boolean(),
      kubeletVersion: z.string(),
      preferredIp: z.string(),
      cpuCapacity: z.string(),
      memoryCapacity: z.string(),
      isSelf: z.boolean(),
      isControlPlane: z.boolean(),
      isDesignatedMaster: z.boolean(),
      platformLabel: z.string(),
      relativeAge: z.string(),
    }),
  ),
  lastSyncLabel: z.string(),
})

export const clusterJoinCommandSchema = z.object({
  command: z.string().min(1),
})
