// Package client is a small typed HTTP client for the PlaceContext management
// API (`/api/v1`). It mirrors the DTOs documented in docs/management-api.md and
// maps non-2xx responses to descriptive Go errors.
package client

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"
)

// ErrNotFound is returned for a 404 so callers (resource Read) can treat the
// resource as gone and remove it from state.
var ErrNotFound = errors.New("placecontext: resource not found (404)")

// Client talks to a single workspace's management API. It is safe for
// concurrent use.
type Client struct {
	baseURL    string
	apiKey     string
	httpClient *http.Client
}

// Option configures a Client.
type Option func(*Client)

// WithHTTPClient overrides the default *http.Client (useful in tests).
func WithHTTPClient(hc *http.Client) Option {
	return func(c *Client) { c.httpClient = hc }
}

// New builds a Client. endpoint may be the workspace root
// (https://acme.placecontext.ai) or already include the /api/v1 suffix; either
// way it is normalized to the /api/v1 base.
func New(endpoint, apiKey string, opts ...Option) *Client {
	c := &Client{
		baseURL: normalizeBaseURL(endpoint),
		apiKey:  apiKey,
		httpClient: &http.Client{
			Timeout: 30 * time.Second,
		},
	}
	for _, o := range opts {
		o(c)
	}
	return c
}

func normalizeBaseURL(endpoint string) string {
	base := strings.TrimRight(strings.TrimSpace(endpoint), "/")
	if strings.HasSuffix(base, "/api/v1") {
		return base
	}
	return base + "/api/v1"
}

// apiError is the documented error body: { "error": "..." }.
type apiError struct {
	Error string `json:"error"`
}

// do performs a request against the management API. body may be nil. On a 2xx
// with a body, out (if non-nil) is JSON-decoded. Non-2xx responses become
// descriptive errors; 404 becomes ErrNotFound.
func (c *Client) do(ctx context.Context, method, path string, body, out any) error {
	var reader io.Reader
	if body != nil {
		buf, err := json.Marshal(body)
		if err != nil {
			return fmt.Errorf("placecontext: encoding request body: %w", err)
		}
		reader = bytes.NewReader(buf)
	}

	req, err := http.NewRequestWithContext(ctx, method, c.baseURL+path, reader)
	if err != nil {
		return fmt.Errorf("placecontext: building request: %w", err)
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	req.Header.Set("Accept", "application/json")
	// Auth: the API accepts either header; send the bearer form.
	req.Header.Set("Authorization", "Bearer "+c.apiKey)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return fmt.Errorf("placecontext: request to %s %s failed: %w", method, path, err)
	}
	defer func() { _ = resp.Body.Close() }()

	respBody, _ := io.ReadAll(resp.Body)

	switch {
	case resp.StatusCode == http.StatusNotFound:
		return ErrNotFound
	case resp.StatusCode == http.StatusUnauthorized:
		return fmt.Errorf("placecontext: 401 unauthorized for %s %s — check api_key / PlaceContext:Api:Key", method, path)
	case resp.StatusCode < 200 || resp.StatusCode >= 300:
		msg := strings.TrimSpace(string(respBody))
		if parsed := parseError(respBody); parsed != "" {
			msg = parsed
		}
		if msg == "" {
			msg = http.StatusText(resp.StatusCode)
		}
		return fmt.Errorf("placecontext: %s %s returned %d: %s", method, path, resp.StatusCode, msg)
	}

	if out != nil && len(respBody) > 0 {
		if err := json.Unmarshal(respBody, out); err != nil {
			return fmt.Errorf("placecontext: decoding response from %s %s: %w", method, path, err)
		}
	}
	return nil
}

func parseError(body []byte) string {
	if len(body) == 0 {
		return ""
	}
	var e apiError
	if err := json.Unmarshal(body, &e); err == nil {
		return e.Error
	}
	return ""
}

// ---- Projects ----

// CreateProject registers a project (idempotent by path). Returns the created
// or existing project.
func (c *Client) CreateProject(ctx context.Context, req CreateProjectRequest) (*Project, error) {
	var p Project
	if err := c.do(ctx, http.MethodPost, "/projects", req, &p); err != nil {
		return nil, err
	}
	return &p, nil
}

// GetProject reads a single project. Returns ErrNotFound if it doesn't exist.
func (c *Client) GetProject(ctx context.Context, id string) (*Project, error) {
	var p Project
	if err := c.do(ctx, http.MethodGet, "/projects/"+id, nil, &p); err != nil {
		return nil, err
	}
	return &p, nil
}

// ListProjects lists every project in the workspace.
func (c *Client) ListProjects(ctx context.Context) ([]Project, error) {
	var ps []Project
	if err := c.do(ctx, http.MethodGet, "/projects", nil, &ps); err != nil {
		return nil, err
	}
	return ps, nil
}

// ---- Jobs ----

// CreateJob creates a job under a project.
func (c *Client) CreateJob(ctx context.Context, projectID string, job Job) (*Job, error) {
	var out Job
	if err := c.do(ctx, http.MethodPost, "/projects/"+projectID+"/jobs", job, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// GetJob reads a single job. Returns ErrNotFound if it doesn't exist.
func (c *Client) GetJob(ctx context.Context, id string) (*Job, error) {
	var out Job
	if err := c.do(ctx, http.MethodGet, "/jobs/"+id, nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// ListJobs lists every job under a project.
func (c *Client) ListJobs(ctx context.Context, projectID string) ([]Job, error) {
	var out []Job
	if err := c.do(ctx, http.MethodGet, "/projects/"+projectID+"/jobs", nil, &out); err != nil {
		return nil, err
	}
	return out, nil
}

// UpdateJob replaces a job's configuration.
func (c *Client) UpdateJob(ctx context.Context, id string, job Job) (*Job, error) {
	var out Job
	if err := c.do(ctx, http.MethodPut, "/jobs/"+id, job, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// DeleteJob permanently deletes a job definition.
func (c *Client) DeleteJob(ctx context.Context, id string) error {
	return c.do(ctx, http.MethodDelete, "/jobs/"+id, nil, nil)
}

// ---- Schedules ----

// CreateSchedule creates a schedule/event trigger on a job within a project.
func (c *Client) CreateSchedule(ctx context.Context, projectID string, req CreateScheduleRequest) (*Schedule, error) {
	var out Schedule
	if err := c.do(ctx, http.MethodPost, "/projects/"+projectID+"/schedules", req, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// GetSchedule reads a single schedule. Returns ErrNotFound if it doesn't exist.
func (c *Client) GetSchedule(ctx context.Context, id string) (*Schedule, error) {
	var out Schedule
	if err := c.do(ctx, http.MethodGet, "/schedules/"+id, nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// ListSchedules lists every schedule under a project.
func (c *Client) ListSchedules(ctx context.Context, projectID string) ([]Schedule, error) {
	var out []Schedule
	if err := c.do(ctx, http.MethodGet, "/projects/"+projectID+"/schedules", nil, &out); err != nil {
		return nil, err
	}
	return out, nil
}

// UpdateSchedule enables or pauses a schedule — the only supported mutation.
func (c *Client) UpdateSchedule(ctx context.Context, id string, enabled bool) (*Schedule, error) {
	var out Schedule
	if err := c.do(ctx, http.MethodPut, "/schedules/"+id, UpdateScheduleRequest{Enabled: enabled}, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// DeleteSchedule permanently removes a schedule.
func (c *Client) DeleteSchedule(ctx context.Context, id string) error {
	return c.do(ctx, http.MethodDelete, "/schedules/"+id, nil, nil)
}
