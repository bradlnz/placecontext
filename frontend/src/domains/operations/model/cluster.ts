export interface ClusterNode {
  name: string
  roles: string[]
  ready: boolean
  kubeletVersion: string
  preferredIp: string
  cpuCapacity: string
  memoryCapacity: string
  isSelf: boolean
  isControlPlane: boolean
  isDesignatedMaster: boolean
  platformLabel: string
  relativeAge: string
}

export interface ClusterPageModel {
  isRealCluster: boolean
  designatedMasterName: string | null
  nodes: ClusterNode[]
  lastSyncLabel: string
}
