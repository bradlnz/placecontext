class ApplicationController < ActionController::Base
  before_action :require_tenant_host!
  before_action :authenticate_portal_user!, unless: -> { controller_name == "health" }

  helper_method :portal_tenant_id

  private

  def require_tenant_host!
    return if request.path == "/healthz"

    custom_domain = request.host.casecmp?(ENV.fetch("PLACE_CONTEXT_PORTAL_DOMAIN"))
    path = portal_path
    shared_host = ENV["PLACE_CONTEXT_PORTAL_SHARED_HOST"].to_s
    shared_origin = !shared_host.empty?
      && request.host.casecmp?(shared_host)
      && (request.path == path || request.path.start_with?("#{path}/"))
    head :misdirected_request unless custom_domain || shared_origin
  end

  def portal_tenant_id
    ENV.fetch("PLACE_CONTEXT_TENANT_ID")
  end

  def portal_path
    ENV.fetch("PLACE_CONTEXT_PORTAL_PATH", "/p/#{ENV.fetch("PLACE_CONTEXT_TENANT_SLUG", "customer")}").sub(%r{/\z}, "")
  end
end
