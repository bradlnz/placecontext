require_relative "boot"

require "rails"
require "active_model/railtie"
require "active_job/railtie"
require "active_record/railtie"
require "action_controller/railtie"
require "action_view/railtie"
require "action_mailer/railtie"
require "action_cable/engine"
require "rails/test_unit/railtie"
require "devise"
require "devise/orm/active_record"

Bundler.require(*Rails.groups)

module CustomerPortal
  class Application < Rails::Application
    config.load_defaults 8.1
    config.autoload_lib(ignore: %w[assets tasks])
    config.active_record.schema_format = :ruby
    config.generators.system_tests = nil
    config.relative_url_root = ENV["RAILS_RELATIVE_URL_ROOT"] if ENV["RAILS_RELATIVE_URL_ROOT"].present?
  end
end
