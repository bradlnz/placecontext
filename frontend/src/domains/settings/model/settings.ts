export interface BrandingSettings {
  productName: string | null
  logoDataUri: string | null
  bgColor: string | null
  panelColor: string | null
  textColor: string | null
  accentColor: string | null
}

export interface LocalitySettings {
  timeZoneId: string
  timeZones: string[]
}

export interface MenuSettingsItem {
  id: string
  defaultLabel: string
  label: string
  order: number
  visible: boolean
  section: string
}

export interface MenuSettings {
  workspace: MenuSettingsItem[]
}

export interface ArtifactFilter {
  id: string
  label: string
  prefixes: string[]
}

export interface ArtifactFilterSettings {
  categories: ArtifactFilter[]
}
