if Rails.env.production? || Rails.env.development? || Rails.env.test?
  Rails.application.config.after_initialize do
    tenant_id = ENV["PLACE_CONTEXT_TENANT_ID"]&.strip
    default_user_name = ENV["PORTAL_DEFAULT_USER_NAME"]&.strip
    default_user_email = ENV["PORTAL_DEFAULT_USER_EMAIL"]&.strip
    default_password = ENV["PORTAL_DEFAULT_USER_PASSWORD"]&.strip

    if tenant_id.blank? || default_user_name.blank? || default_user_email.blank?
      next
    end

    candidate_email = default_user_email
    candidate_email = default_user_name if candidate_email.blank? && URI::MailTo::EMAIL_REGEXP.match?(default_user_name)
    unless URI::MailTo::EMAIL_REGEXP.match?(candidate_email)
      Rails.logger.warn("Skipping default portal user seed for tenant #{tenant_id}: invalid email #{candidate_email.inspect}")
      next
    end

    begin
      portal_user = PortalUser.find_or_initialize_by(tenant_id: tenant_id, email: candidate_email)
      portal_user.role = :admin if portal_user.new_record?

      if portal_user.new_record?
        portal_user.enabled = true
        portal_user.password = default_password.presence || SecureRandom.base58(32)
        portal_user.password_confirmation = portal_user.password
      end

      changed = portal_user.new_record? || portal_user.changed?
      if changed && portal_user.save
        if portal_user.previously_new_record? && portal_user.persisted?
          portal_user.send_reset_password_instructions
        end
      end
    rescue StandardError => ex
      Rails.logger.warn(
        "Skipping default portal user seed for tenant #{tenant_id}: #{ex.class}: #{ex.message}"
      )
    end
  end
end
