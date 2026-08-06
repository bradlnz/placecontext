class LeadsController < ApplicationController
  before_action :require_manager_or_admin!, only: %i[new create edit update destroy]

  def index
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first["id"]
    @leads = @project_id ? crm.clients(@project_id).select { |client| client["lifecycleStage"] == "Lead" } : []
  end

  def show
    redirect_to client_path(params[:id], project_id: params[:project_id])
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end

  def require_manager_or_admin!
    return if current_portal_user&.can_manage_client_access?

    redirect_to leads_path, alert: "Manager access is required to change CRM data."
  end
end
