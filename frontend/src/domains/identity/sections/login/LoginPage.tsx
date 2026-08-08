import { useSuspenseQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'

import { identityContextQuery } from '../../api/identity-query-options'
import { AuthBrand } from '../auth-shell/AuthBrand'

export function LoginPage() {
  const { data: identity } = useSuspenseQuery(identityContextQuery)
  const [searchParams] = useSearchParams()
  const error = searchParams.get('error')
  const returnUrl = searchParams.get('returnUrl')
  const email = searchParams.get('email') ?? ''

  return (
    <section className="dccard auth-card">
      <title>placecontext — sign in</title>
      <AuthBrand />
      <h1 className="auth-title">Sign in</h1>
      <p className="auth-sub">Enter this workspace&apos;s admin (or member) credentials.</p>

      <form action="/auth/login" className="auth-form" method="post">
        <input name={identity.antiforgeryFieldName} type="hidden" value={identity.antiforgeryToken} />
        {error === null ? null : <div className="auth-error" role="alert">{error}</div>}
        {returnUrl === null ? null : <input name="returnUrl" type="hidden" value={returnUrl} />}
        <label className="dcfield">
          <span>Email</span>
          <input autoComplete="username" autoFocus className="dcinput" defaultValue={email} name="email" required type="email" />
        </label>
        <label className="dcfield">
          <span>Password</span>
          <input autoComplete="current-password" className="dcinput" name="password" required type="password" />
        </label>
        <button className="dcbtn primary auth-submit" type="submit">Sign in</button>
      </form>
    </section>
  )
}
