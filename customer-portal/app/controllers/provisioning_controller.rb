require "securerandom"
require "uri"

class ProvisioningController < ActionController::API
  before_action :authenticate_provisioner!

  def create_user
    email = params[:username].to_s.strip.presence || params.require(:email).to_s.strip.downcase
    password = params[:password].to_s.strip
    requested_role = params[:role].to_s.strip
    tenant_id = ENV.fetch("PLACE_CONTEXT_TENANT_ID")
    return render json: { error: "valid email is required" }, status: :bad_request unless email.match?(URI::MailTo::EMAIL_REGEXP)

    user = PortalUser.find_or_initialize_by(email: email, tenant_id: tenant_id)
    if user.new_record?
      user.tenant_id = tenant_id
      user.role = first_user?(tenant_id) ? "admin" : normalize_role(requested_role, "member")
    elsif requested_role.present? && PortalUser.roles.key?(requested_role)
      user.role = requested_role
    end
    user.enabled = true
    if user.new_record?
      user.password = password.presence || SecureRandom.base58(40)
    elsif password.present?
      user.password = password
    end
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
    expected_tenant = ENV.fetch("PLACE_CONTEXT_TENANT_ID")
    valid_key = expected.present? && presented.present? &&
      expected.bytesize == presented.bytesize &&
      ActiveSupport::SecurityUtils.secure_compare(presented, expected)
    valid_tenant = tenant == expected_tenant
    return if valid_key && valid_tenant

    render json: { error: "Invalid provisioning credentials." }, status: :unauthorized
  end

  def first_user?(tenant_id)
    !PortalUser.exists?(tenant_id: tenant_id)
  end

  def normalize_role(value, fallback)
    PortalUser.roles.key?(value) ? value : fallback
  end
end
