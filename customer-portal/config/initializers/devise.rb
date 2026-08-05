Devise.setup do |config|
  config.mailer_sender = ENV.fetch("PORTAL_MAILER_SENDER", "portal@placecontext.local")
  config.parent_controller = "ApplicationController"
  config.case_insensitive_keys = [:email]
  config.strip_whitespace_keys = [:email]
  config.password_length = 12..128
  config.reconfirmable = true
  config.expire_all_remember_me_on_sign_out = true
  config.stretches = Rails.env.test? ? 1 : 12
end
