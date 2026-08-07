class PortalClient < ApplicationRecord
  LIFECYCLE_STAGES = %w[Lead Qualified Proposal Won Lost].freeze
  DEFAULT_PROJECT_ID = "default".freeze

  validates :tenant_id, presence: true
  validates :project_id, presence: true
  validates :name, presence: true
  validates :lifecycle_stage, inclusion: { in: LIFECYCLE_STAGES }

  scope :for_tenant, ->(tenant_id) { where(tenant_id: tenant_id) }
  scope :for_project, ->(project_id) { where(project_id: project_id) }
  scope :leads, -> { where(lifecycle_stage: "Lead") }

  before_validation :ensure_default_project

  private

  def ensure_default_project
    project = project_id.to_s.strip
    self.project_id = DEFAULT_PROJECT_ID if project.blank?
  end
end
