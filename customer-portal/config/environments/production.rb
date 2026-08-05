require_relative "../environment"

Rails.application.configure do
  config.enable_reloading = false
  config.eager_load = true
  config.consider_all_requests_local = false
  config.force_ssl = ENV.fetch("RAILS_FORCE_SSL", "true") == "true"
  config.log_level = :info
  config.active_record.dump_schema_after_migration = false
  config.action_mailer.default_url_options = { host: ENV.fetch("PLACE_CONTEXT_PORTAL_DOMAIN") }
  config.action_mailer.delivery_method = :smtp
  config.action_mailer.smtp_settings = {
    address: ENV.fetch("SMTP_ADDRESS", "localhost"),
    port: ENV.fetch("SMTP_PORT", "587").to_i,
    domain: ENV.fetch("SMTP_DOMAIN", "placecontext.local"),
    user_name: ENV["SMTP_USERNAME"],
    password: ENV["SMTP_PASSWORD"],
    authentication: ENV.fetch("SMTP_AUTHENTICATION", "plain"),
    enable_starttls_auto: ENV.fetch("SMTP_ENABLE_STARTTLS", "true") == "true"
  }
end
