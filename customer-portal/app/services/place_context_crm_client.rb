require "json"
require "net/http"
require "uri"

class PlaceContextCrmClient
  def initialize(current_user:)
    @current_user = current_user
  end

  def projects
    get("/api/customer-portal/projects")
  end

  def clients(project_id)
    get("/api/customer-portal/clients", project_id: project_id)
  end

  def client(id, project_id)
    get("/api/customer-portal/clients/#{id}", project_id: project_id)
  end

  def create_client(attributes)
    post("/api/customer-portal/clients", attributes)
  end

  def update_client(id, attributes)
    put("/api/customer-portal/clients/#{id}", attributes)
  end

  def delete_client(id)
    delete("/api/customer-portal/clients/#{id}")
  end

  def job_chains(project_id)
    get("/api/customer-portal/job-chains", project_id: project_id)
  end

  def job_chain(chain_id, project_id)
    get("/api/customer-portal/job-chains/#{chain_id}", project_id: project_id)
  end

  def run_job_chain(chain_id, project_id, input_payload: nil, step_payload_overrides: {})
    post("/api/customer-portal/job-chains/#{chain_id}/run", {
      project_id: project_id,
      input_payload: input_payload,
      step_payload_overrides: step_payload_overrides
    })
  end

  def chain_run(run_id)
    get("/api/customer-portal/chain-runs/#{run_id}")
  end

  private

  def get(path, query = {})
    request(Net::HTTP::Get, path, query: query)
  end

  def post(path, body)
    request(Net::HTTP::Post, path, body: body)
  end

  def put(path, body)
    request(Net::HTTP::Put, path, body: body)
  end

  def delete(path)
    request(Net::HTTP::Delete, path)
  end

  def request(method, path, query: {}, body: nil)
    base = URI.join(ENV.fetch("PLACE_CONTEXT_CORE_API_URL").end_with?("/") ? ENV.fetch("PLACE_CONTEXT_CORE_API_URL") : "#{ENV.fetch("PLACE_CONTEXT_CORE_API_URL")}/", path.delete_prefix("/"))
    base.query = URI.encode_www_form(query.transform_keys { |key| key.to_s.camelize(:lower) }) unless query.empty?
    http = Net::HTTP.new(base.host, base.port)
    http.use_ssl = base.scheme == "https"
    http.open_timeout = 5
    http.read_timeout = 20

    request = method.new(base)
    request["Accept"] = "application/json"
    request["Content-Type"] = "application/json"
    request["Authorization"] = "Bearer #{ENV.fetch("PLACE_CONTEXT_CORE_API_KEY")}"
    request["X-PlaceContext-Tenant-Id"] = @current_user.tenant_id.to_s
    request.body = JSON.generate(camelize_body(body)) if body
    response = http.request(request)
    return {} if response.code.to_i == 204
    payload = response.body.to_s.empty? ? {} : JSON.parse(response.body)
    return payload if response.is_a?(Net::HTTPSuccess)

    raise "Placecontext CRM API returned #{response.code}: #{payload.is_a?(Hash) ? payload.values.first : payload}"
  end

  def camelize_body(value)
    case value
    when Hash
      value.transform_keys do |key|
        key.is_a?(Integer) ? key : key.to_s.camelize(:lower)
      end.transform_values { |v| camelize_body(v) }
    when Array
      value.map { |item| camelize_body(item) }
    else
      value
    end
  end
end
