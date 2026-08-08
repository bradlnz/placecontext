import { useEffect } from 'react'

import { navigateToLegacyPath } from '../../../../shared/navigation/legacy-navigation'

export function OnboardingPage() {
  useEffect(() => {
    void navigateToLegacyPath('/wiki/getting-started')
  }, [])

  return (
    <div className="page onboarding-redirect" role="status">
      Opening the getting started guide…
    </div>
  )
}
