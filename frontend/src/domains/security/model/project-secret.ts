export interface ProjectSecret {
  name: string
  createdAt: string
  createdAtDisplay: string
}

export interface CreateProjectSecretCommand {
  name: string
  value: string
}
