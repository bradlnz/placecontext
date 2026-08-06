class PortalPathPrefixMiddleware
  def initialize(app)
    @app = app
  end

  def call(env)
    portal_path = ENV["PLACE_CONTEXT_PORTAL_PATH"]
    path = env["PATH_INFO"].to_s
    set_script_name = false
    should_prefix_path = true

    if portal_path.present?
      prefix = portal_path.to_s.chomp("/")
      shared_host = ENV["PLACE_CONTEXT_PORTAL_SHARED_HOST"].to_s
      host = env["HTTP_HOST"].to_s.split(":", 2).first
      should_prefix_path = shared_host.empty? || host.casecmp?(shared_host) == true

      assets_prefix = "#{prefix}/assets"
      if path == "/assets" || path == assets_prefix || path.start_with?("#{assets_prefix}/")
        env["PATH_INFO"] = path.sub(%r{\A#{Regexp.escape(assets_prefix)}}, "/assets")
        set_script_name = true
      elsif path.start_with?(prefix)
        stripped = path.delete_prefix(prefix)
        stripped = "/" if stripped.empty?
        stripped = "/" + stripped unless stripped.start_with?("/")
        env["PATH_INFO"] = stripped
        set_script_name = true
      end
    else
      # When no portal path is configured, leave PATH_INFO unchanged.
    end

    env["SCRIPT_NAME"] = prefix if set_script_name && portal_path.present? && should_prefix_path
    @app.call(env)
  end
end

Rails.application.config.middleware.insert_before 0, PortalPathPrefixMiddleware
