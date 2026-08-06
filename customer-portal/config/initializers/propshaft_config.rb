portal_path = ENV.fetch("PLACE_CONTEXT_PORTAL_PATH", "/")
relative_url_root = ENV.fetch("RAILS_RELATIVE_URL_ROOT", portal_path).to_s.strip

relative_url_root = relative_url_root.sub(%r{/+\z}, "")
relative_url_root = "/#{relative_url_root}" if relative_url_root.present? && !relative_url_root.start_with?("/")

Rails.application.config.relative_url_root = relative_url_root.presence
Rails.application.config.assets.prefix = "/assets"
