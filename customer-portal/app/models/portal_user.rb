class PortalUser < ApplicationRecord
  devise :database_authenticatable, :recoverable, :rememberable, :validatable

  enum :role, { member: 0, manager: 1, admin: 2 }, default: :member

  scope :enabled, -> { where(enabled: true) }

  validates :tenant_id, presence: true
  validate :tenant_matches_configuration

  def manager_or_admin?
    manager? || admin?
  end

  def can_manage_client_access?
    manager_or_admin?
  end

  def can_manage_portal_users?
    admin?
  end

  def active_for_authentication?
    super && enabled?
  end

  def inactive_message
    enabled? ? super : :account_disabled
  end

  private

  def tenant_matches_configuration
    return if tenant_id.to_s == ENV.fetch("PLACE_CONTEXT_TENANT_ID")

    errors.add(:tenant_id, "does not match the configured tenant")
  end
end
