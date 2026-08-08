# This file is auto-generated from the current state of the database. Instead
# of editing this file, please use the migrations feature of Active Record to
# incrementally modify your database, and then regenerate this schema definition.
#
# This file is the source Rails uses to define your schema when running `bin/rails
# db:schema:load`. When creating a new database, `bin/rails db:schema:load` tends to
# be faster and is potentially less error prone than running all of your
# migrations from scratch. Old migrations may fail to apply correctly if those
# migrations use external dependencies or application code.
#
# It's strongly recommended that you check this file into your version control system.

ActiveRecord::Schema[8.1].define(version: 2026_08_07_080000) do
  # These are extensions that must be enabled in order to support this database
  enable_extension "pg_catalog.plpgsql"
  enable_extension "pgcrypto"

  create_table "portal_clients", id: :uuid, default: -> { "gen_random_uuid()" }, force: :cascade do |t|
    t.string "company"
    t.datetime "created_at", null: false
    t.string "email"
    t.string "lifecycle_stage", default: "Lead", null: false
    t.string "name", null: false
    t.text "notes"
    t.string "phone"
    t.string "project_id", default: "default", null: false
    t.uuid "tenant_id", null: false
    t.datetime "updated_at", null: false
    t.index ["tenant_id", "lifecycle_stage"], name: "index_portal_clients_on_tenant_id_and_lifecycle_stage"
    t.index ["tenant_id", "project_id"], name: "index_portal_clients_on_tenant_id_and_project_id"
    t.index ["tenant_id"], name: "index_portal_clients_on_tenant_id"
  end

  create_table "portal_users", id: :uuid, default: -> { "gen_random_uuid()" }, force: :cascade do |t|
    t.datetime "created_at", null: false
    t.string "email", default: "", null: false
    t.boolean "enabled", default: true, null: false
    t.string "encrypted_password", default: "", null: false
    t.datetime "remember_created_at"
    t.datetime "reset_password_sent_at"
    t.string "reset_password_token"
    t.integer "role", default: 0, null: false
    t.uuid "tenant_id", null: false
    t.datetime "updated_at", null: false
    t.index ["email"], name: "index_portal_users_on_email", unique: true
    t.index ["reset_password_token"], name: "index_portal_users_on_reset_password_token", unique: true
    t.index ["tenant_id", "email"], name: "index_portal_users_on_tenant_id_and_email", unique: true
    t.index ["tenant_id", "enabled"], name: "index_portal_users_on_tenant_id_and_enabled"
  end
end
