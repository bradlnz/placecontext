class ApplicationController < ActionController::Base
  rescue_from PlaceContextCrmClient::ApiError, with: :handle_place_context_crm_error

  before_action :require_tenant_host!
  before_action :authenticate_portal_user!,
    unless: -> { controller_name == "health" || static_asset_request? }
  before_action :enforce_portal_tenant!

  layout :resolve_layout

  helper_method :portal_tenant_id, :portal_path
  helper_method :accessible_portal_clients

  private

  def resolve_layout
    devise_controller? ? "devise" : "application"
  end

  def require_tenant_host!
    return if request.path == "/healthz" || request.path == "#{portal_path}/healthz"

    custom_domain = request.host.casecmp?(ENV.fetch("PLACE_CONTEXT_PORTAL_DOMAIN"))
    path = portal_path
    shared_host = ENV["PLACE_CONTEXT_PORTAL_SHARED_HOST"].to_s
    request_path_base = request.respond_to?(:path_base) ? request.path_base : request.script_name.to_s
    request_path = "#{request_path_base}#{request.path}"
    shared_origin = !shared_host.empty?
      && request.host.casecmp?(shared_host)
      && (request_path == path || request_path.start_with?("#{path}/"))
    head :misdirected_request unless custom_domain || shared_origin
  end

  def portal_tenant_id
    ENV.fetch("PLACE_CONTEXT_TENANT_ID")
  end

  def portal_path
    ENV.fetch("PLACE_CONTEXT_PORTAL_PATH", "/p/#{ENV.fetch("PLACE_CONTEXT_TENANT_SLUG", "customer")}").sub(%r{/\z}, "")
  end

  def static_asset_request?
    path = request.path.to_s
    path == "/assets" || path.start_with?("/assets/")
      || path == "#{portal_path}/assets"
      || path.start_with?("#{portal_path}/assets/")
  end

  def enforce_portal_tenant!
    return if current_portal_user.blank?
    return if current_portal_user.tenant_id.to_s == portal_tenant_id

    sign_out(current_portal_user)
    redirect_to new_portal_user_session_path, alert: "Invalid tenant session. Sign in again to continue."
  end

  def handle_place_context_crm_error(exception)
    status_code = exception.respond_to?(:status) ? exception.status : nil
    message =
      if status_code == 401
        "Customer CRM authentication failed (401). Verify PLACE_CONTEXT_CORE_API_KEY (or PlaceContext__CustomerPortal__ApiKey) is configured for this portal."
      else
        exception.message
      end

    Rails.logger.error("PlaceContext CRM API error: #{exception.message}")
    redirect_to root_path, alert: message
  end

  def accessible_portal_clients
    @accessible_portal_clients ||= begin
      tenant_clients = PortalClient.where(tenant_id: portal_tenant_id)
      return tenant_clients if current_portal_user&.can_manage_client_access?

      email = current_portal_user&.email.to_s.strip.downcase
      return PortalClient.none if email.blank?

      tenant_clients.where("LOWER(email) = ?", email)
    end
  end
end
