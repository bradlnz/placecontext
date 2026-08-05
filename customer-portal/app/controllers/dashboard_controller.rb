class DashboardController < ApplicationController
  def show
    @projects = crm.projects
    @project_id = @projects.first&.dig("id")
    clients = @project_id ? crm.clients(@project_id) : []
    @clients_count = clients.size
    @leads_count = clients.count { |client| client["lifecycleStage"] == "Lead" }
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
