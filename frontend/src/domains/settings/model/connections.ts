export interface ConnectionProject {
  id: string
  name: string
  hasExternalDatabase: boolean
  hasExternalIndex: boolean
}

export interface ConnectionsSettings {
  projects: ConnectionProject[]
  sslModes: string[]
}

export interface ExternalDatabaseInput {
  host: string
  port: string
  database: string
  username: string
  password: string
  sslMode: string
}

export interface ExternalIndexInput {
  endpoint: string
  username: string
  password: string
  index: string
}
