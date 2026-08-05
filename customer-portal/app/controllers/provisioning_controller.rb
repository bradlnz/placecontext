require "securerandom"
require "uri"

class ProvisioningController < ActionController::API
  before_action :authenticate_provisioner!

  def create_user
    email = params.require(:email).to_s.strip.downcase
    role = params.fetch(:role, "member").to_s
    return render json: { error: "valid email is required" }, status: :bad_request unless email.match?(URI::MailTo::EMAIL_REGEXP)

    user = PortalUser.find_or_initialize_by(email: email)
    user.tenant_id = ENV.fetch("PLACE_CONTEXT_TENANT_ID")
    user.role = %w[member manager].include?(role) ? role : "member"
    user.enabled = true
    user.password = SecureRandom.base58(40) if user.new_record?
    user.save!
    user.send_reset_password_instructions

    render json: { id: user.id, email: user.email, role: user.role, status: "invited" }, status: :created
  rescue ActiveRecord::RecordInvalid => e
    render json: { error: e.record.errors.full_messages.to_sentence }, status: :unprocessable_entity
  end

  def impersonate
    email = params.require(:email).to_s.strip.downcase
    user = PortalUser.find_by(tenant_id: ENV.fetch("PLACE_CONTEXT_TENANT_ID"), email: email)
    return render json: { error: "No portal account for #{email} — invite the client first." }, status: :not_found unless user

    token = Rails.application.message_verifier(:portal_impersonation).generate(
      { "user_id" => user.id, "exp" => 10.minutes.from_now.to_i }
    )
    render json: { url: "/impersonate/#{user.id}?t=#{token}" }, status: :ok
  end

  private

  def authenticate_provisioner!
    expected = ENV.fetch("PLACE_CONTEXT_PROVISIONING_KEY")
    presented = request.headers["X-PlaceContext-Provisioning-Key"].to_s
    tenant = request.headers["X-PlaceContext-Tenant-Id"].to_s
    valid = ActiveSupport::SecurityUtils.secure_compare(presented, expected) && tenant == ENV.fetch("PLACE_CONTEXT_TENANT_ID")
    head :unauthorized unless valid
  end
end
