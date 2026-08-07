class ClientsController < ApplicationController
  before_action :require_manager_or_admin!, only: %i[new create edit update destroy]
  before_action :load_project_options, only: %i[index new]
  before_action :set_project_id, only: %i[index new]

  def index
    @clients = tenant_clients.where(project_id: @project_id).order(updated_at: :desc)
  end

  def show
    @client = tenant_client
    @project_id = params[:project_id].presence || @client.project_id
  end

  def new
    @project_id = params[:project_id].presence || @project_id
    @client = PortalClient.new(project_id: @project_id)
  end

  def create
    client = tenant_clients.new(client_attributes)
    client.save!
    redirect_to client_path(client, project_id: client.project_id), notice: "Client saved."
  rescue StandardError => e
    redirect_to new_client_path(project_id: client_attributes["project_id"]), alert: e.message
  end

  def edit
    @client = tenant_client
    @project_id = params[:project_id].presence || @client.project_id
  end

  def update
    client = tenant_client
    client.update!(client_attributes)
    redirect_to client_path(client, project_id: client.project_id), notice: "Client updated."
  rescue StandardError => e
    redirect_to edit_client_path(params[:id], project_id: client_attributes["project_id"]), alert: e.message
  end

  def destroy
    tenant_client.destroy!
    redirect_to clients_path(project_id: params[:project_id]), notice: "Client deleted."
  rescue StandardError => e
    redirect_to clients_path, alert: e.message
  end

  private

  def tenant_clients
    @tenant_clients ||= accessible_portal_clients
  end

  def tenant_client
    tenant_clients.find(params[:id])
  end

  def load_project_options
    project_ids = tenant_clients.pluck(:project_id).reject(&:blank?).uniq.sort
    project_ids = [PortalClient::DEFAULT_PROJECT_ID] if project_ids.empty?
    @projects = project_ids.map { |project_id| { "id" => project_id, "name" => project_label(project_id) } }
  end

  def project_label(project_id)
    project_id == PortalClient::DEFAULT_PROJECT_ID ? "Default project" : project_id
  end

  def set_project_id
    @project_id = params[:project_id].presence || @projects.first["id"]
  end

  def client_attributes
    permitted = params.require(:client).permit(:project_id, :name, :company, :email, :phone, :lifecycle_stage, :notes).to_h
    tenant_id = portal_tenant_id.to_s
    permitted.merge("tenant_id" => tenant_id, "project_id" => (permitted["project_id"].presence || PortalClient::DEFAULT_PROJECT_ID))
  end

  def require_manager_or_admin!
    return if current_portal_user&.can_manage_client_access?

    redirect_to clients_path, alert: "Manager access is required to change CRM data."
  end
end
