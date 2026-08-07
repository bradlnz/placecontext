Rails.application.routes.draw do
  legacy_portal_path = ENV.fetch("PLACE_CONTEXT_PORTAL_PATH", "/")
  get "/portal_users/sign_in", to: redirect("#{legacy_portal_path.sub(%r{/\\z}, "")}/login")
  devise_for :portal_users,
    path: "",
    path_names: { sign_in: "login", sign_out: "logout", sign_up: "register", password: "password" }
  post "/api/provision/users", to: "provisioning#create_user"
  post "/:slug/api/provision/users", to: "provisioning#create_user"
  post "/api/provision/impersonate", to: "provisioning#impersonate"
  post "/:slug/api/provision/impersonate", to: "provisioning#impersonate"
  get "/impersonate/:id", to: "impersonation#login"
  resources :portal_users, only: %i[index new create]

  root "dashboard#show"
  get "/healthz", to: "health#show"
  resources :clients
  resources :leads, only: %i[index show]
  resources :automations, only: %i[index show]
  post "/automations/:id/run", to: "automations#run", as: :run_automation
  get "/automation_runs/:id", to: "automations#run_status", as: :automation_run

  get "/:slug", to: "dashboard#show"
end
