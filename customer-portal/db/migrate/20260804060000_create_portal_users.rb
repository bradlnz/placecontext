class CreatePortalUsers < ActiveRecord::Migration[8.1]
  def change
    enable_extension "pgcrypto" unless extension_enabled?("pgcrypto")

    create_table :portal_users, id: :uuid do |t|
      t.string :email, null: false, default: ""
      t.string :encrypted_password, null: false, default: ""
      t.string :reset_password_token
      t.datetime :reset_password_sent_at
      t.datetime :remember_created_at
      t.uuid :tenant_id, null: false
      t.integer :role, null: false, default: 0
      t.boolean :enabled, null: false, default: true
      t.timestamps null: false
    end

    add_index :portal_users, :email, unique: true
    add_index :portal_users, :reset_password_token, unique: true
    add_index :portal_users, [:tenant_id, :email], unique: true
    add_index :portal_users, [:tenant_id, :enabled]
  end
end
