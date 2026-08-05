# Propshaft's server strips the configured prefix from asset requests and then
# looks up the remaining path. With the default "/assets" prefix, the leading
# slash is left behind ("/application.css") and the lookup fails. Using a
# trailing slash fixes asset serving in development.
#
# When the portal is served under a relative URL root (e.g. /p/:tenant),
# include that root in the asset prefix so the Propshaft dev server still
# matches requests and stylesheet_link_tag generates the correct URL.
relative_root = ENV["RAILS_RELATIVE_URL_ROOT"].to_s.sub(%r{/\z}, "")
Rails.application.config.assets.prefix = "#{relative_root}/assets/"
Rails.application.config.assets.relative_url_root = relative_root.presence
