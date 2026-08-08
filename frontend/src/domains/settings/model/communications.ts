export interface CommunicationProvider {
  id: string
  channel: 'email' | 'sms'
  kind: 'postmark' | 'sendgrid' | 'twilio'
  name: string
  enabled: boolean
  isDefault: boolean
  useForTwoFactor: boolean
  authType: 'none' | 'bearer' | 'header' | 'basic'
  authHeaderName: string | null
  vaultProjectId: string | null
  apiKeySecretName: string | null
  settingsJson: string
  createdAt: string
  updatedAt: string
}

export interface CommunicationProject {
  id: string
  name: string
}

export interface CommunicationSecret {
  name: string
  createdAt: string
}

export interface CommunicationsSettings {
  providers: CommunicationProvider[]
  projects: CommunicationProject[]
}

export interface CommunicationProviderInput {
  channel: CommunicationProvider['channel']
  kind: CommunicationProvider['kind']
  name: string
  enabled: boolean
  authType: CommunicationProvider['authType']
  authHeaderName: string | null
  vaultProjectId: string | null
  apiKeySecretName: string | null
  settingsJson: string
}

export interface CommunicationProviderDraft extends CommunicationProviderInput {
  fromEmail: string
  fromName: string
  messageStream: string
  accountSid: string
  fromNumber: string
  endpoint: string
}
