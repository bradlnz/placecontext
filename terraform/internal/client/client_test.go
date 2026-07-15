package client

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func newTestServer(t *testing.T, h http.HandlerFunc) (*Client, *httptest.Server) {
	t.Helper()
	srv := httptest.NewServer(h)
	t.Cleanup(srv.Close)
	c := New(srv.URL, "test-key", WithHTTPClient(srv.Client()))
	return c, srv
}

func TestNormalizeBaseURL(t *testing.T) {
	cases := map[string]string{
		"https://acme.placecontext.ai":         "https://acme.placecontext.ai/api/v1",
		"https://acme.placecontext.ai/":        "https://acme.placecontext.ai/api/v1",
		"https://acme.placecontext.ai/api/v1":  "https://acme.placecontext.ai/api/v1",
		"https://acme.placecontext.ai/api/v1/": "https://acme.placecontext.ai/api/v1",
		"  http://acme.localhost:7700  ":       "http://acme.localhost:7700/api/v1",
	}
	for in, want := range cases {
		if got := normalizeBaseURL(in); got != want {
			t.Errorf("normalizeBaseURL(%q) = %q, want %q", in, got, want)
		}
	}
}

func TestCreateProject_SendsAuthAndBody(t *testing.T) {
	var gotAuth, gotMethod, gotPath string
	var gotBody CreateProjectRequest

	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotAuth = r.Header.Get("Authorization")
		gotMethod = r.Method
		gotPath = r.URL.Path
		_ = json.NewDecoder(r.Body).Decode(&gotBody)
		w.Header().Set("Location", "/api/v1/projects/proj-1")
		w.WriteHeader(http.StatusCreated)
		_ = json.NewEncoder(w).Encode(Project{ID: "proj-1", Name: "storefront-api", Path: gotBody.Path, Status: "Registered"})
	})

	p, err := c.CreateProject(context.Background(), CreateProjectRequest{Path: "/code/storefront-api", Name: "storefront-api"})
	if err != nil {
		t.Fatalf("CreateProject: %v", err)
	}
	if gotAuth != "Bearer test-key" {
		t.Errorf("auth header = %q, want %q", gotAuth, "Bearer test-key")
	}
	if gotMethod != http.MethodPost || gotPath != "/api/v1/projects" {
		t.Errorf("method/path = %s %s, want POST /api/v1/projects", gotMethod, gotPath)
	}
	if gotBody.Path != "/code/storefront-api" {
		t.Errorf("request path field = %q", gotBody.Path)
	}
	if p.ID != "proj-1" || p.Status != "Registered" {
		t.Errorf("decoded project = %+v", p)
	}
}

func TestGetProject_NotFoundMapsToErrNotFound(t *testing.T) {
	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	_, err := c.GetProject(context.Background(), "missing")
	if !errors.Is(err, ErrNotFound) {
		t.Fatalf("expected ErrNotFound, got %v", err)
	}
}

func TestUnauthorizedError(t *testing.T) {
	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusUnauthorized)
	})
	_, err := c.ListProjects(context.Background())
	if err == nil || errors.Is(err, ErrNotFound) {
		t.Fatalf("expected a non-notfound error, got %v", err)
	}
	if !strings.Contains(err.Error(), "401") {
		t.Errorf("error should mention 401, got %v", err)
	}
}

func TestValidationError_ParsesErrorBody(t *testing.T) {
	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusBadRequest)
		_ = json.NewEncoder(w).Encode(map[string]string{"error": "path is required"})
	})
	_, err := c.CreateProject(context.Background(), CreateProjectRequest{})
	if err == nil {
		t.Fatal("expected error")
	}
	if !strings.Contains(err.Error(), "path is required") {
		t.Errorf("error should include server message, got %v", err)
	}
}

func TestCreateJob_RoundTrip(t *testing.T) {
	var gotPath string
	var gotJob Job
	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		_ = json.NewDecoder(r.Body).Decode(&gotJob)
		resp := gotJob
		resp.ID = "job-1"
		resp.ProjectID = "proj-1"
		resp.MapSourceKind = ptr("code")
		resp.CreatedAt = "2026-07-15T12:00:00+00:00"
		resp.UpdatedAt = "2026-07-15T12:00:00+00:00"
		w.WriteHeader(http.StatusCreated)
		_ = json.NewEncoder(w).Encode(resp)
	})

	in := Job{
		Name:         "etl-sync",
		MapRuntimeID: ptr("python"),
		MapFiles:     []FileEntry{{Path: "main.py", Content: "print('hi')\n"}},
		MapEntrypoint: ptr("main.py"),
		InputPayloads: []string{`{"batch":1}`},
		ConcurrencyLimit: 4,
		SuccessExitCodes: []int{0},
		ReturnType:       "Table",
	}
	out, err := c.CreateJob(context.Background(), "proj-1", in)
	if err != nil {
		t.Fatalf("CreateJob: %v", err)
	}
	if gotPath != "/api/v1/projects/proj-1/jobs" {
		t.Errorf("path = %q", gotPath)
	}
	if gotJob.Name != "etl-sync" || len(gotJob.MapFiles) != 1 || gotJob.MapFiles[0].Path != "main.py" {
		t.Errorf("server received unexpected job: %+v", gotJob)
	}
	if out.ID != "job-1" || out.MapSourceKind == nil || *out.MapSourceKind != "code" {
		t.Errorf("decoded job unexpected: %+v", out)
	}
}

func TestUpdateSchedule_SendsEnabled(t *testing.T) {
	var gotBody UpdateScheduleRequest
	var gotMethod, gotPath string
	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotMethod = r.Method
		gotPath = r.URL.Path
		_ = json.NewDecoder(r.Body).Decode(&gotBody)
		w.WriteHeader(http.StatusOK)
		_ = json.NewEncoder(w).Encode(Schedule{ID: "sched-1", Enabled: gotBody.Enabled, Kind: "Schedule"})
	})

	s, err := c.UpdateSchedule(context.Background(), "sched-1", false)
	if err != nil {
		t.Fatalf("UpdateSchedule: %v", err)
	}
	if gotMethod != http.MethodPut || gotPath != "/api/v1/schedules/sched-1" {
		t.Errorf("method/path = %s %s", gotMethod, gotPath)
	}
	if gotBody.Enabled != false || s.Enabled != false {
		t.Errorf("enabled round-trip failed: sent %v, got %v", gotBody.Enabled, s.Enabled)
	}
}

func TestDeleteSchedule_204(t *testing.T) {
	c, _ := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodDelete {
			t.Errorf("method = %s", r.Method)
		}
		w.WriteHeader(http.StatusNoContent)
	})
	if err := c.DeleteSchedule(context.Background(), "sched-1"); err != nil {
		t.Fatalf("DeleteSchedule: %v", err)
	}
}

func ptr(s string) *string { return &s }
