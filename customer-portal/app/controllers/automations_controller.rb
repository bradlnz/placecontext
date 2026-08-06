class AutomationsController < ApplicationController
  before_action :require_manager_or_admin!, only: :run

  def index
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first["id"]
    @chains = @project_id ? crm.job_chains(@project_id) : []
  end

  def show
    @projects = crm.projects
    @project_id = params[:project_id].presence || @projects.first["id"]
    @chain = crm.job_chain(params[:id], @project_id)
  rescue StandardError => e
    redirect_to automations_path(project_id: @project_id), alert: e.message
  end

  def run
    project_id = params[:project_id].presence || crm.projects.first["id"]
    step_overrides = build_step_payload_overrides
    result = crm.run_job_chain(params[:id], project_id, step_payload_overrides: step_overrides)
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

  def require_manager_or_admin!
    return if current_portal_user&.can_manage_client_access?

    redirect_to automations_path, alert: "Manager access is required to run automations."
  end
end
