Rails.application.routes.draw do
  devise_for :portal_users
  post "/api/provision/users", to: "provisioning#create_user"

  root "dashboard#show"
  get "/healthz", to: "health#show"
  resources :clients
  resources :leads, only: %i[index show]
  resources :automations, only: %i[index show]
  post "/automations/:id/run", to: "automations#run", as: :run_automation
  get "/automation_runs/:id", to: "automations#run_status", as: :automation_run
end
