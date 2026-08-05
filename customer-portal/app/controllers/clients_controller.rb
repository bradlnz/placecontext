class ClientsController < ApplicationController
  before_action :require_manager!, only: %i[new create edit update destroy]

  def index
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first["id"]
    @clients = @project_id ? crm.clients(@project_id) : []
  end

  def show
    @project_id = params[:project_id]
    @client = crm.client(params[:id], @project_id)
  rescue StandardError => e
    redirect_to clients_path, alert: e.message
  end

  def new
    @projects = crm.projects
    @project_id = params[:project_id] || @projects.first["id"]
    @client = {}
  end

  def create
    result = crm.create_client(client_attributes)
    redirect_to client_path(result["id"], project_id: result["projectId"]), notice: "Client saved."
  rescue StandardError => e
    redirect_to new_client_path(project_id: params[:client]["project_id"]), alert: e.message
  end

  def edit
    @project_id = params[:project_id]
    @client = crm.client(params[:id], @project_id)
  end

  def update
    result = crm.update_client(params[:id], client_attributes)
    redirect_to client_path(result["id"], project_id: result["projectId"]), notice: "Client updated."
  rescue StandardError => e
    redirect_to edit_client_path(params[:id], project_id: params[:client]["project_id"]), alert: e.message
  end

  def destroy
    crm.delete_client(params[:id])
    redirect_to clients_path(project_id: params[:project_id]), notice: "Client deleted."
  rescue StandardError => e
    redirect_to clients_path, alert: e.message
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end

  def client_attributes
    params.require(:client).permit(:project_id, :name, :company, :email, :phone, :lifecycle_stage, :notes).to_h
  end

  def require_manager!
    return if current_portal_user.manager?

    redirect_to clients_path, alert: "Manager access is required to change CRM data."
  end
end
