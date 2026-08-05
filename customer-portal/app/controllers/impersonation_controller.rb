class ImpersonationController < ActionController::Base
  skip_before_action :authenticate_portal_user!

  def login
    payload = Rails.application.message_verifier(:portal_impersonation).verified(params[:t].to_s)
    valid = payload &&
      payload["user_id"].to_s == params[:id].to_s &&
      payload["exp"].to_i >= Time.now.to_i

    if valid
      user = PortalUser.find_by(id: params[:id], tenant_id: ENV.fetch("PLACE_CONTEXT_TENANT_ID"))
      if user
        sign_in(user)
        redirect_to root_path
        return
      end
    end
    head :unauthorized
  end
end
