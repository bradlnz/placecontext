class DashboardController < ApplicationController
  def show
    clients = accessible_portal_clients
    @clients_count = clients.count
    @leads_count = clients.where(lifecycle_stage: "Lead").count
    @projects = crm.projects
    @project_id = @projects.first&.dig("id")
    @automations_count = @project_id ? crm.job_chains(@project_id).size : 0
  rescue StandardError
    @projects = []
    @clients_count = 0
    @leads_count = 0
    @automations_count = 0
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end
end
