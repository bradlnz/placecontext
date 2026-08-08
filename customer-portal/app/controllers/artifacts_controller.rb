class ArtifactsController < ApplicationController
  def index
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first&.dig("id")
    @portal_clients = portal_clients_for_project(@project_id)
    @client = selected_client
    @artifacts = @client ? crm.client_artifacts(@client["id"]) : []
  end

  def open
    project_id = params.require(:project_id)
    client = portal_clients_for_project(project_id).find { |item| item["id"].to_s == params.require(:client_id).to_s }
    return head :not_found unless client

    artifact = crm.client_artifacts(client["id"]).find { |item| item["id"].to_s == params[:id].to_s }
    return head :not_found unless artifact

    file = crm.download_client_artifact(client["id"], artifact["id"])
    send_data file[:body],
      filename: artifact["title"],
      type: file[:content_type],
      disposition: "inline"
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end

  def selected_client
    requested = params[:client_id].to_s.strip
    return @portal_clients.find { |client| client["id"].to_s == requested } if requested.present?

    @portal_clients.first
  end

  def portal_clients_for_project(project_id)
    return [] if project_id.blank?

    clients = crm.clients(project_id)
    return clients.sort_by { |client| client["name"].to_s.downcase } if current_portal_user.can_manage_client_access?

    email = current_portal_user.email.to_s.strip.downcase
    clients.select { |client| client["email"].to_s.strip.downcase == email }
  end
end
