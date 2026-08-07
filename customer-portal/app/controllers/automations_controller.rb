class AutomationsController < ApplicationController
  def index
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first["id"]
    @chains = @project_id ? crm.job_chains(@project_id) : []

    if @project_id && current_portal_user && !current_portal_user.can_manage_client_access?
      @chains = filter_chains_by_client_assignments(@chains, @project_id)
    end
  end

  def show
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first["id"]
    @chain = crm.job_chain(params[:id], @project_id)
    if @project_id && current_portal_user && !current_portal_user.can_manage_client_access?
      return redirect_to automations_path(project_id: @project_id), alert: "You do not have access to this automation." unless chain_assigned_to_portal_client?(
        chain_id: params[:id],
        project_id: @project_id
      )
    end
  rescue StandardError => e
    redirect_to automations_path(project_id: @project_id), alert: e.message
  end

  def run
    project_id = params[:project_id].presence || crm.projects.first["id"]
    client_id = params[:client_id].presence

    if !current_portal_user.can_manage_client_access?
      client = portal_client_for_user(project_id: project_id)
      return redirect_to(automations_path(project_id: project_id), alert: "No accessible customer record for this portal user.") unless client

      requested_client_id = client_id.to_s.strip
      if requested_client_id.present? && requested_client_id != client["id"].to_s
        return redirect_to(
          automations_path(project_id: project_id),
          alert: "This automation can only be run for your customer record."
        )
      end
      client_id = client["id"]
    end

    return redirect_to(automations_path(project_id: project_id), alert: "This automation is not assigned to you.") unless
      current_portal_user.can_manage_client_access? || chain_assigned_to_portal_client?(
        chain_id: params[:id],
        project_id: project_id,
        client_id: client_id
      )

    step_overrides = build_step_payload_overrides
    result = crm.run_job_chain(
      params[:id],
      project_id,
      step_payload_overrides: step_overrides,
      client_id: client_id
    )
    redirect_to automation_run_path(result["id"], project_id: project_id), notice: "Automation started."
  rescue StandardError => e
    redirect_to automation_path(params[:id], project_id: project_id), alert: e.message
  end

  def run_status
    @run = crm.chain_run(params[:id])
    @project_id = params[:project_id]
  rescue StandardError => e
    redirect_to automations_path(project_id: params[:project_id]), alert: e.message
  end

  private

  def crm
    @crm ||= PlaceContextCrmClient.new(current_user: current_portal_user)
  end

  def portal_client_for_user(project_id: nil)
    return nil if current_portal_user.can_manage_client_access?

    scope = accessible_portal_clients
    scope = scope.where(project_id: project_id) if project_id.present?
    scope.order(updated_at: :desc).first
  end

  def chain_assigned_to_portal_client?(chain_id:, project_id:, client_id: nil)
    client = client_id.present? ? accessible_portal_clients.find_by(id: client_id) : portal_client_for_user(project_id: project_id)
    return false unless client && project_id

    assigned_chain_ids = crm.client_job_chains(project_id, client["id"]).map(&:to_s)
    assigned_chain_ids.include?(chain_id.to_s)
  end

  def filter_chains_by_client_assignments(chains, project_id)
    return [] if chains.blank?

    client = portal_client_for_user(project_id: project_id)
    return [] unless client

    assigned_ids = crm.client_job_chains(project_id, client["id"]).map(&:to_s)
    chains.select { |chain| assigned_ids.include?(chain["id"]&.to_s) }
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
