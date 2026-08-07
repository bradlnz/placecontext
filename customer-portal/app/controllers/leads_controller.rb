class LeadsController < ApplicationController
  before_action :require_manager_or_admin!, only: %i[new create edit update destroy]

  def index
    project_ids = tenant_clients.pluck(:project_id).reject(&:blank?).uniq.sort
    project_ids = [PortalClient::DEFAULT_PROJECT_ID] if project_ids.empty?
    @projects = project_ids.map { |project_id| { "id" => project_id, "name" => project_label(project_id) } }
    @project_id = params[:project_id].presence || @projects.first["id"]
    @leads = tenant_clients.where(project_id: @project_id, lifecycle_stage: "Lead").order(updated_at: :desc)
  end

  def show
    redirect_to client_path(params[:id], project_id: params[:project_id])
  end

  private

  def tenant_clients
    @tenant_clients ||= accessible_portal_clients
  end

  def project_label(project_id)
    project_id == PortalClient::DEFAULT_PROJECT_ID ? "Default project" : project_id
  end

  def require_manager_or_admin!
    return if current_portal_user&.can_manage_client_access?

    redirect_to leads_path, alert: "Manager access is required to change CRM data."
  end
end
