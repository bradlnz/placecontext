class AutomationsController < ApplicationController
  def index
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first&.dig("id")
    load_customer_context
    @chains = @project_id && @client ? filter_chains_by_client_assignments(crm.job_chains(@project_id), @project_id, @client) : []
  end

  def show
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first&.dig("id")
    load_customer_context
    return redirect_to automations_path(project_id: @project_id), alert: "Select a customer to view automations." unless @client

    @chain = crm.job_chain(params[:id], @project_id)
    return redirect_to automations_path(project_id: @project_id, client_id: @client["id"]), alert: "This automation is not available for this customer." unless chain_assigned_to_portal_client?(
      chain_id: params[:id],
      project_id: @project_id,
      client: @client
    )
  rescue StandardError => e
    redirect_to automations_path(project_id: @project_id), alert: e.message
  end

  def run
    project_id = params[:project_id].presence || crm.projects.first&.dig("id")
    client = selected_portal_client(project_id: project_id, client_id: params[:client_id])
    return redirect_to(automations_path(project_id: project_id), alert: "Select a customer before running an automation.") unless client

    return redirect_to(automations_path(project_id: project_id, client_id: client["id"]), alert: "This automation is not assigned to this customer.") unless
      chain_assigned_to_portal_client?(chain_id: params[:id], project_id: project_id, client: client)

    step_overrides = build_step_payload_overrides
    result = crm.run_job_chain(
      params[:id],
      project_id,
      step_payload_overrides: step_overrides,
      client_id: client["id"]
    )
    redirect_to automation_run_path(result["id"], project_id: project_id, client_id: client["id"]), notice: "Automation started for #{client["name"]}."
  rescue StandardError => e
    redirect_to automation_path(params[:id], project_id: project_id, client_id: params[:client_id]), alert: e.message
  end

  def run_status
    @run = crm.chain_run(params[:id])
    @project_id = params[:project_id]
    @client = selected_portal_client(project_id: @project_id, client_id: params[:client_id])
  rescue StandardError => e
    redirect_to automations_path(project_id: params[:project_id]), alert: e.message
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end

  def selected_portal_client(project_id:, client_id: nil)
    scope = portal_clients_for_project(project_id)
    requested = client_id.to_s.strip
    return scope.find { |client| client["id"].to_s == requested } if requested.present?

    scope.first
  end

  def chain_assigned_to_portal_client?(chain_id:, project_id:, client:)
    return false unless client && project_id

    assigned_chain_ids = crm.client_job_chains(project_id, client["id"]).map(&:to_s)
    assigned_chain_ids.include?(chain_id.to_s)
  end

  def filter_chains_by_client_assignments(chains, project_id, client)
    return [] if chains.blank?
    assigned_ids = crm.client_job_chains(project_id, client["id"]).map(&:to_s)
    chains.select { |chain| assigned_ids.include?(chain["id"]&.to_s) }
  end

  def load_customer_context
    @portal_clients = portal_clients_for_project(@project_id)
    @client = selected_portal_client(project_id: @project_id, client_id: params[:client_id])
  end

  def portal_clients_for_project(project_id)
    return [] if project_id.blank?

    clients = crm.clients(project_id)
    return clients.sort_by { |client| client["name"].to_s.downcase } if current_portal_user.can_manage_client_access?

    email = current_portal_user.email.to_s.strip.downcase
    clients.select { |client| client["email"].to_s.strip.downcase == email }
  end

  def build_step_payload_overrides
    overrides = {}
    step_params = params[:steps] || {}
    step_params.each do |step_index, fields|
      next unless fields.is_a?(Hash) && fields[:params].is_a?(Hash)

      values = fields[:params].to_h.compact_blank
      next if values.empty?

      overrides[step_index.to_i] = JSON.generate(values)
    end
    overrides
  end

end
