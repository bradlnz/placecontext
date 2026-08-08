import { useSuspenseQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'

import { identityContextQuery } from '../../api/identity-query-options'
import { AuthBrand } from '../auth-shell/AuthBrand'

export function SetupPage() {
  const { data: identity } = useSuspenseQuery(identityContextQuery)
  const [searchParams] = useSearchParams()
  const error = searchParams.get('error')

  if (identity.configured) {
    return (
      <section className="dccard auth-card">
        <title>placecontext — workspace configured</title>
        <AuthBrand />
        <h1 className="auth-title">Already set up</h1>
        <p className="auth-sub">This workspace already has an admin account.</p>
        <a className="dcbtn primary auth-submit" href="/login">Go to sign in</a>
      </section>
    )
  }

  return (
    <section className="dccard auth-card">
      <title>placecontext — set up this workspace</title>
      <AuthBrand />
      <h1 className="auth-title">Set up this workspace</h1>
      <p className="auth-sub">Create the admin account before anything else is reachable. There is no default password — you choose one now.</p>

      <form action="/auth/setup" className="auth-form" method="post">
        <input name={identity.antiforgeryFieldName} type="hidden" value={identity.antiforgeryToken} />
        {error === null ? null : <div className="auth-error" role="alert">{error}</div>}
        <label className="dcfield">
          <span>Admin email <span className="required">*</span></span>
          <input autoFocus className="dcinput" name="email" placeholder="you@example.com" required type="email" />
        </label>
        <label className="dcfield">
          <span>Display name</span>
          <input className="dcinput" name="displayName" placeholder="Optional — defaults to the email's local part" type="text" />
        </label>
        <label className="dcfield">
          <span>Password <span className="required">*</span> <code>min 12 characters</code></span>
          <input autoComplete="new-password" className="dcinput" minLength={12} name="password" required type="password" />
        </label>
        <label className="dcfield">
          <span>Confirm password <span className="required">*</span></span>
          <input autoComplete="new-password" className="dcinput" minLength={12} name="confirmPassword" required type="password" />
        </label>
        <button className="dcbtn primary auth-submit" type="submit">Create admin account</button>
        <div className="auth-hint">Avoid common or reused passwords — this account can manage the whole workspace.</div>
      </form>
    </section>
  )
}
