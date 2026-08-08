class DashboardController < ApplicationController
  def show
    clients = accessible_portal_clients
    @clients_count = clients.count
    @leads_count = clients.where(lifecycle_stage: "Lead").count
    @projects = crm.projects
    @project_id = @projects.first&.dig("id")
    core_clients = @project_id ? crm.clients(@project_id) : []
    unless current_portal_user.can_manage_client_access?
      email = current_portal_user.email.to_s.strip.downcase
      core_clients = core_clients.select { |client| client["email"].to_s.strip.downcase == email }
    end
    @client = core_clients.first
    assigned_ids = @client ? crm.client_job_chains(@project_id, @client["id"]).map(&:to_s) : []
    @automations_count = @project_id ? crm.job_chains(@project_id).count { |chain| assigned_ids.include?(chain["id"].to_s) } : 0
    @artifacts_count = @client ? crm.client_artifacts(@client["id"]).size : 0
  rescue StandardError
    @projects = []
    @clients_count = 0
    @leads_count = 0
    @automations_count = 0
    @artifacts_count = 0
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end
end
