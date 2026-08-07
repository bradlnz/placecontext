class CreatePortalClients < ActiveRecord::Migration[8.1]
  def change
    create_table :portal_clients, id: :uuid do |t|
      t.uuid :tenant_id, null: false
      t.string :project_id, null: false, default: "default"
      t.string :name, null: false
      t.string :company
      t.string :email
      t.string :phone
      t.string :lifecycle_stage, null: false, default: "Lead"
      t.text :notes

      t.timestamps null: false
    end

    add_index :portal_clients, :tenant_id
    add_index :portal_clients, %i[tenant_id project_id]
    add_index :portal_clients, %i[tenant_id lifecycle_stage]
  end
end
