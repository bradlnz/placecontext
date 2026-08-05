# Ensure Propshaft asset helpers are available in views even when the railtie's
# on_load hook has already fired (common when adding propshaft to an existing app).
ActiveSupport.on_load(:action_view) do
  include Propshaft::Helper
end

if defined?(ActionView::Base) && !ActionView::Base.ancestors.include?(Propshaft::Helper)
  ActionView::Base.include(Propshaft::Helper)
end
