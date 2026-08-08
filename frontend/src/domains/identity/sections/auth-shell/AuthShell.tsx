import { Outlet } from 'react-router-dom'

export function AuthShell() {
  return (
    <div className="auth-shell" data-theme="dark" id="dcshell">
      <main className="auth-wrap"><Outlet /></main>
    </div>
  )
}
