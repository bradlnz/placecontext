class PortalUsersController < ApplicationController
  before_action :require_portal_admin!

  def index
    @portal_users = PortalUser.where(tenant_id: portal_tenant_id).order(:email)
    @portal_user = PortalUser.new
  end

  def new
    @portal_user = PortalUser.new(role: initial_role)
  end

  def create
    @portal_user = PortalUser.new(portal_user_attributes)
    @portal_user.tenant_id = portal_tenant_id
    @portal_user.role = :admin if @portal_user.new_record? && first_portal_user_for_tenant?
    set_portal_user_password

    if @portal_user.save
      send_portal_invite(@portal_user)
      redirect_to portal_users_path, notice: "Portal login created for #{@portal_user.email}. A reset link has been sent."
      return
    end

    @portal_users = PortalUser.where(tenant_id: portal_tenant_id).order(:email)
    flash.now[:alert] = @portal_user.errors.full_messages.to_sentence
    render :index, status: :unprocessable_entity
  end

  private

  def portal_user_attributes
    params.require(:portal_user).permit(:email, :role, :password, :password_confirmation)
  end

  def set_portal_user_password
    if @portal_user.password.blank?
      generated = SecureRandom.base58(32)
      @portal_user.password = generated
      @portal_user.password_confirmation = generated
    elsif @portal_user.password_confirmation.blank?
      @portal_user.password_confirmation = @portal_user.password
    end

    @portal_user.enabled = true
  end

  def require_portal_admin!
    return if current_portal_user&.can_manage_portal_users?

    redirect_to root_path, alert: "Admin access is required to manage portal users."
  end

  def initial_role
    first_portal_user_for_tenant? ? :admin : :member
  end

  def first_portal_user_for_tenant?
    !PortalUser.exists?(tenant_id: portal_tenant_id)
  end

  def send_portal_invite(portal_user)
    portal_user.send_reset_password_instructions
  rescue StandardError => e
    Rails.logger.warn("Skipping portal invite email for #{portal_user.email}: #{e.class}: #{e.message}")
  end
end
