package provider

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func newTestClient(t *testing.T, handler http.Handler) *Client {
	t.Helper()
	server := httptest.NewServer(handler)
	t.Cleanup(server.Close)
	return NewClient(server.URL, "test-key")
}

func requireAuth(t *testing.T, r *http.Request) {
	t.Helper()
	if got := r.Header.Get("Authorization"); got != "Bearer test-key" {
		t.Errorf("Authorization = %q, want %q", got, "Bearer test-key")
	}
}

func writeJSON(t *testing.T, w http.ResponseWriter, status int, value any) {
	t.Helper()
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(value); err != nil {
		t.Fatalf("encode response: %v", err)
	}
}

func TestCreateAndReadProject(t *testing.T) {
	handler := http.NewServeMux()
	handler.HandleFunc("POST /api/v1/projects", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		if got := r.Header.Get("Content-Type"); got != "application/json" {
			t.Errorf("Content-Type = %q, want application/json", got)
		}
		var body map[string]any
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Fatalf("decode body: %v", err)
		}
		if body["path"] != "data/revenue" {
			t.Errorf("path = %v, want data/revenue", body["path"])
		}
		if body["name"] != "Revenue" {
			t.Errorf("name = %v, want Revenue", body["name"])
		}
		writeJSON(t, w, http.StatusCreated, Project{
			ID: "11111111-1111-1111-1111-111111111111", Name: "Revenue",
			Path: "data/revenue", Status: "Registered", IsGraphified: true,
		})
	})
	handler.HandleFunc("GET /api/v1/projects/{id}", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		if r.PathValue("id") != "11111111-1111-1111-1111-111111111111" {
			t.Errorf("unexpected id %q", r.PathValue("id"))
		}
		writeJSON(t, w, http.StatusOK, Project{
			ID: "11111111-1111-1111-1111-111111111111", Name: "Revenue",
			Path: "data/revenue", Status: "Graphified", IsGraphified: true,
		})
	})

	client := newTestClient(t, handler)
	name := "Revenue"
	created, err := client.CreateProject(context.Background(), "data/revenue", &name)
	if err != nil {
		t.Fatalf("CreateProject: %v", err)
	}
	if created.ID != "11111111-1111-1111-1111-111111111111" || !created.IsGraphified {
		t.Fatalf("unexpected project: %+v", created)
	}

	read, err := client.GetProject(context.Background(), created.ID)
	if err != nil {
		t.Fatalf("GetProject: %v", err)
	}
	if read.Status != "Graphified" {
		t.Errorf("status = %q, want Graphified", read.Status)
	}
}

func TestGetProjectNotFound(t *testing.T) {
	client := newTestClient(t, http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Error(w, `{"error":"not found"}`, http.StatusNotFound)
	}))

	_, err := client.GetProject(context.Background(), "missing")
	if err == nil {
		t.Fatal("expected an error")
	}
	if !IsNotFound(err) {
		t.Fatalf("IsNotFound(%v) = false, want true", err)
	}
}

func TestAPIErrorIncludesResponseBody(t *testing.T) {
	client := newTestClient(t, http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Error(w, `{"error":"the cron expression is invalid"}`, http.StatusBadRequest)
	}))

	err := client.DeleteJob(context.Background(), "job-id")
	if err == nil {
		t.Fatal("expected an error")
	}
	if !strings.Contains(err.Error(), "cron expression is invalid") {
		t.Errorf("error %q does not include the response body", err)
	}
	var apiErr *APIError
	if !asAPIError(err, &apiErr) || apiErr.Status != http.StatusBadRequest {
		t.Errorf("expected a 400 APIError, got %v", err)
	}
}

func TestCreateJobSendsCamelCaseAndOmitsDefaults(t *testing.T) {
	var captured map[string]any
	handler := http.NewServeMux()
	handler.HandleFunc("POST /api/v1/projects/{projectId}/jobs", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		if r.PathValue("projectId") != "project-1" {
			t.Errorf("projectId = %q", r.PathValue("projectId"))
		}
		body, _ := io.ReadAll(r.Body)
		if err := json.Unmarshal(body, &captured); err != nil {
			t.Fatalf("decode body: %v", err)
		}
		imageValue := captured["mapImage"].(string)
		writeJSON(t, w, http.StatusCreated, Job{
			ID: "job-1", ProjectID: "project-1", Name: "Nightly",
			MapSourceKind: "image", MapImage: &imageValue,
			MapFiles: []JobCodeFile{}, InputPayloads: []string{}, MapEnv: map[string]string{},
			ReduceFiles: []JobCodeFile{}, ReduceEnv: map[string]string{},
			ConcurrencyLimit: 4, SuccessExitCodes: []int{0}, PartialExitCodes: []int{},
			Parameters: []JobParameter{}, PostJobActions: []string{},
			ReturnType: "Json", McpConnectionIDs: []string{},
			CreatedAt: "2026-01-01T00:00:00Z", UpdatedAt: "2026-01-01T00:00:00Z",
		})
	})

	image := "alpine:3"
	client := newTestClient(t, handler)
	job, err := client.CreateJob(context.Background(), "project-1", JobRequest{
		Name:     "Nightly",
		MapImage: &image,
	})
	if err != nil {
		t.Fatalf("CreateJob: %v", err)
	}
	if captured["mapImage"] != "alpine:3" {
		t.Errorf("mapImage = %v, want alpine:3", captured["mapImage"])
	}
	for _, omitted := range []string{"concurrencyLimit", "returnType", "allowNetworkEgress"} {
		if _, ok := captured[omitted]; ok {
			t.Errorf("expected %q to be omitted so the server default applies", omitted)
		}
	}
	if job.ConcurrencyLimit != 4 || job.ReturnType != "Json" {
		t.Errorf("unexpected defaults: %+v", job)
	}
}

func TestDeleteJobTreats204AsSuccess(t *testing.T) {
	handler := http.NewServeMux()
	handler.HandleFunc("DELETE /api/v1/jobs/{id}", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		if r.PathValue("id") != "job-1" {
			t.Errorf("id = %q", r.PathValue("id"))
		}
		w.WriteHeader(http.StatusNoContent)
	})

	if err := newTestClient(t, handler).DeleteJob(context.Background(), "job-1"); err != nil {
		t.Fatalf("DeleteJob: %v", err)
	}
}

func TestScheduleCreateUpdateDelete(t *testing.T) {
	handler := http.NewServeMux()
	handler.HandleFunc("POST /api/v1/projects/{projectId}/schedules", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		var body CreateScheduleRequest
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Fatalf("decode body: %v", err)
		}
		if body.JobID != "job-1" || body.Kind != "Schedule" || body.CronExpression == nil ||
			*body.CronExpression != "0 2 * * *" {
			t.Errorf("unexpected create body: %+v", body)
		}
		writeJSON(t, w, http.StatusCreated, Schedule{
			ID: "sched-1", ProjectID: "project-1", JobID: "job-1", Name: "Nightly",
			Kind: "Schedule", Enabled: true, CronExpression: body.CronExpression,
			CreatedAt: "2026-01-01T00:00:00Z",
		})
	})
	handler.HandleFunc("PUT /api/v1/schedules/{id}", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		var body UpdateScheduleRequest
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Fatalf("decode body: %v", err)
		}
		if body.Enabled == nil || *body.Enabled {
			t.Errorf("expected enabled=false, got %+v", body)
		}
		writeJSON(t, w, http.StatusOK, Schedule{
			ID: "sched-1", ProjectID: "project-1", JobID: "job-1", Name: *body.Name,
			Kind: "Schedule", Enabled: false, CronExpression: body.CronExpression,
			CreatedAt: "2026-01-01T00:00:00Z",
		})
	})
	handler.HandleFunc("DELETE /api/v1/schedules/{id}", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		w.WriteHeader(http.StatusNoContent)
	})

	client := newTestClient(t, handler)
	cron := "0 2 * * *"
	schedule, err := client.CreateSchedule(context.Background(), "project-1", CreateScheduleRequest{
		JobID: "job-1", Name: "Nightly", Kind: "Schedule", CronExpression: &cron,
	})
	if err != nil {
		t.Fatalf("CreateSchedule: %v", err)
	}
	if schedule.ID != "sched-1" || !schedule.Enabled {
		t.Fatalf("unexpected schedule: %+v", schedule)
	}

	disabled := false
	name := "Nightly"
	updated, err := client.UpdateSchedule(context.Background(), schedule.ID, UpdateScheduleRequest{
		Name: &name, CronExpression: &cron, Enabled: &disabled,
	})
	if err != nil {
		t.Fatalf("UpdateSchedule: %v", err)
	}
	if updated.Enabled {
		t.Error("expected the updated schedule to be disabled")
	}
	if err := client.DeleteSchedule(context.Background(), schedule.ID); err != nil {
		t.Fatalf("DeleteSchedule: %v", err)
	}
}

func TestListProjectsFindsByPath(t *testing.T) {
	handler := http.NewServeMux()
	handler.HandleFunc("GET /api/v1/projects", func(w http.ResponseWriter, r *http.Request) {
		requireAuth(t, r)
		writeJSON(t, w, http.StatusOK, []Project{
			{ID: "1", Name: "A", Path: "a", Status: "Registered"},
			{ID: "2", Name: "B", Path: "b", Status: "Registered"},
		})
	})

	projects, err := newTestClient(t, handler).ListProjects(context.Background())
	if err != nil {
		t.Fatalf("ListProjects: %v", err)
	}
	if len(projects) != 2 || projects[1].Path != "b" {
		t.Fatalf("unexpected projects: %+v", projects)
	}
}

// asAPIError mirrors errors.As without importing errors in every test.
func asAPIError(err error, target **APIError) bool {
	for err != nil {
		if apiErr, ok := err.(*APIError); ok {
			*target = apiErr
			return true
		}
		type unwrapper interface{ Unwrap() error }
		u, ok := err.(unwrapper)
		if !ok {
			return false
		}
		err = u.Unwrap()
	}
	return false
}
