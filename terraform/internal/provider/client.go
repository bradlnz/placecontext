package provider

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"
)

// Client talks to the PlaceContext management API (/api/v1).
//
// Authentication is a single workspace-scoped API key sent as
// `Authorization: Bearer <key>` (the API also accepts `X-Api-Key`). The base
// URL selects the workspace: managed deployments scope the key by subdomain
// (e.g. https://acme.placecontext.io), while a single-tenant self-hosted
// deployment can use its own origin (e.g. http://localhost:7700).
type Client struct {
	baseURL string
	apiKey  string
	http    *http.Client
}

// APIError is a non-2xx response from the management API.
type APIError struct {
	Method string
	Path   string
	Status int
	Body   string
}

func (e *APIError) Error() string {
	body := strings.TrimSpace(e.Body)
	if body == "" {
		return fmt.Sprintf("%s %s returned HTTP %d", e.Method, e.Path, e.Status)
	}
	return fmt.Sprintf("%s %s returned HTTP %d: %s", e.Method, e.Path, e.Status, body)
}

// IsNotFound reports whether err is a 404 from the management API.
func IsNotFound(err error) bool {
	var apiErr *APIError
	return errors.As(err, &apiErr) && apiErr.Status == http.StatusNotFound
}

// NewClient builds a management API client. baseURL is trimmed of a trailing
// slash so resource paths can be appended directly.
func NewClient(baseURL, apiKey string) *Client {
	return &Client{
		baseURL: strings.TrimRight(baseURL, "/"),
		apiKey:  apiKey,
		http: &http.Client{
			Timeout: 60 * time.Second,
		},
	}
}

func (c *Client) do(ctx context.Context, method, requestPath string, body, out any) error {
	var reader io.Reader
	if body != nil {
		payload, err := json.Marshal(body)
		if err != nil {
			return fmt.Errorf("encode request body: %w", err)
		}
		reader = bytes.NewReader(payload)
	}

	endpoint := c.baseURL + requestPath
	req, err := http.NewRequestWithContext(ctx, method, endpoint, reader)
	if err != nil {
		return fmt.Errorf("build request: %w", err)
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("Accept", "application/json")
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}

	resp, err := c.http.Do(req)
	if err != nil {
		return fmt.Errorf("call %s %s: %w", method, requestPath, err)
	}
	defer resp.Body.Close()

	data, err := io.ReadAll(io.LimitReader(resp.Body, 1<<20))
	if err != nil {
		return fmt.Errorf("read response: %w", err)
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return &APIError{Method: method, Path: requestPath, Status: resp.StatusCode, Body: string(data)}
	}
	if out != nil && len(bytes.TrimSpace(data)) > 0 {
		if err := json.Unmarshal(data, out); err != nil {
			return fmt.Errorf("decode response from %s %s: %w", method, requestPath, err)
		}
	}
	return nil
}

func escape(id string) string { return url.PathEscape(id) }

// ── Projects ────────────────────────────────────────────────────────────────

type Project struct {
	ID           string `json:"id"`
	Name         string `json:"name"`
	Path         string `json:"path"`
	Status       string `json:"status"`
	IsGraphified bool   `json:"isGraphified"`
}

type createProjectRequest struct {
	Path string  `json:"path"`
	Name *string `json:"name"`
}

func (c *Client) ListProjects(ctx context.Context) ([]Project, error) {
	var projects []Project
	err := c.do(ctx, http.MethodGet, "/api/v1/projects", nil, &projects)
	return projects, err
}

func (c *Client) GetProject(ctx context.Context, id string) (*Project, error) {
	var project Project
	if err := c.do(ctx, http.MethodGet, "/api/v1/projects/"+escape(id), nil, &project); err != nil {
		return nil, err
	}
	return &project, nil
}

// CreateProject is idempotent by path: re-creating a known path returns the
// existing project (the API still responds 201).
func (c *Client) CreateProject(ctx context.Context, path string, name *string) (*Project, error) {
	var project Project
	err := c.do(ctx, http.MethodPost, "/api/v1/projects", createProjectRequest{Path: path, Name: name}, &project)
	if err != nil {
		return nil, err
	}
	return &project, nil
}

// ── Jobs ────────────────────────────────────────────────────────────────────

type JobCodeFile struct {
	Path    string `json:"path"`
	Content string `json:"content"`
}

type JobParameter struct {
	Name     string   `json:"name"`
	Label    *string  `json:"label,omitempty"`
	Required *bool    `json:"required,omitempty"`
	Type     *string  `json:"type,omitempty"`
	Options  []string `json:"options,omitempty"`
}

// JobRequest is the full desired state sent on create and update. Pointer
// fields are omitted when nil so the API applies its documented defaults
// (concurrency_limit=4, return_type="Json", success_exit_codes=[0], ...).
type JobRequest struct {
	Name               string            `json:"name"`
	Description        *string           `json:"description"`
	MapImage           *string           `json:"mapImage"`
	MapRuntimeID       *string           `json:"mapRuntimeId"`
	MapSource          *string           `json:"mapSource"`
	MapEntrypoint      *string           `json:"mapEntrypoint"`
	MapFiles           []JobCodeFile     `json:"mapFiles,omitempty"`
	InputPayloads      []string          `json:"inputPayloads,omitempty"`
	MapEnv             map[string]string `json:"mapEnv,omitempty"`
	ReduceImage        *string           `json:"reduceImage"`
	ReduceRuntimeID    *string           `json:"reduceRuntimeId"`
	ReduceSource       *string           `json:"reduceSource"`
	ReduceEntrypoint   *string           `json:"reduceEntrypoint"`
	ReduceFiles        []JobCodeFile     `json:"reduceFiles,omitempty"`
	ReduceEnv          map[string]string `json:"reduceEnv,omitempty"`
	ConcurrencyLimit   *int              `json:"concurrencyLimit,omitempty"`
	SuccessExitCodes   []int             `json:"successExitCodes,omitempty"`
	PartialExitCodes   []int             `json:"partialExitCodes,omitempty"`
	AllowNetworkEgress *bool             `json:"allowNetworkEgress,omitempty"`
	AllowAPIInvocation *bool             `json:"allowApiInvocation,omitempty"`
	Parameters         []JobParameter    `json:"parameters,omitempty"`
	PostJobActions     []string          `json:"postJobActions,omitempty"`
	ReturnType         *string           `json:"returnType,omitempty"`
	ReturnFileName     *string           `json:"returnFileName"`
	RetryCount         *int              `json:"retryCount,omitempty"`
	RetryDelaySeconds  *int              `json:"retryDelaySeconds,omitempty"`
	McpConnectionIDs   []string          `json:"mcpConnectionIds,omitempty"`
}

type Job struct {
	ID                 string            `json:"id"`
	ProjectID          string            `json:"projectId"`
	Name               string            `json:"name"`
	Description        *string           `json:"description"`
	MapSourceKind      string            `json:"mapSourceKind"`
	MapImage           *string           `json:"mapImage"`
	MapRuntimeID       *string           `json:"mapRuntimeId"`
	MapSource          *string           `json:"mapSource"`
	MapEntrypoint      *string           `json:"mapEntrypoint"`
	MapFiles           []JobCodeFile     `json:"mapFiles"`
	InputPayloads      []string          `json:"inputPayloads"`
	MapEnv             map[string]string `json:"mapEnv"`
	ReduceSourceKind   *string           `json:"reduceSourceKind"`
	ReduceImage        *string           `json:"reduceImage"`
	ReduceRuntimeID    *string           `json:"reduceRuntimeId"`
	ReduceSource       *string           `json:"reduceSource"`
	ReduceEntrypoint   *string           `json:"reduceEntrypoint"`
	ReduceFiles        []JobCodeFile     `json:"reduceFiles"`
	ReduceEnv          map[string]string `json:"reduceEnv"`
	ConcurrencyLimit   int               `json:"concurrencyLimit"`
	SuccessExitCodes   []int             `json:"successExitCodes"`
	PartialExitCodes   []int             `json:"partialExitCodes"`
	AllowNetworkEgress bool              `json:"allowNetworkEgress"`
	AllowAPIInvocation bool              `json:"allowApiInvocation"`
	Parameters         []JobParameter    `json:"parameters"`
	PostJobActions     []string          `json:"postJobActions"`
	ReturnType         string            `json:"returnType"`
	ReturnFileName     *string           `json:"returnFileName"`
	RetryCount         int               `json:"retryCount"`
	RetryDelaySeconds  int               `json:"retryDelaySeconds"`
	McpConnectionIDs   []string          `json:"mcpConnectionIds"`
	CreatedAt          string            `json:"createdAt"`
	UpdatedAt          string            `json:"updatedAt"`
}

func (c *Client) GetJob(ctx context.Context, id string) (*Job, error) {
	var job Job
	if err := c.do(ctx, http.MethodGet, "/api/v1/jobs/"+escape(id), nil, &job); err != nil {
		return nil, err
	}
	return &job, nil
}

func (c *Client) CreateJob(ctx context.Context, projectID string, request JobRequest) (*Job, error) {
	var job Job
	err := c.do(ctx, http.MethodPost, "/api/v1/projects/"+escape(projectID)+"/jobs", request, &job)
	if err != nil {
		return nil, err
	}
	return &job, nil
}

func (c *Client) UpdateJob(ctx context.Context, id string, request JobRequest) (*Job, error) {
	var job Job
	err := c.do(ctx, http.MethodPut, "/api/v1/jobs/"+escape(id), request, &job)
	if err != nil {
		return nil, err
	}
	return &job, nil
}

func (c *Client) DeleteJob(ctx context.Context, id string) error {
	return c.do(ctx, http.MethodDelete, "/api/v1/jobs/"+escape(id), nil, nil)
}

// ── Schedules ───────────────────────────────────────────────────────────────

type CreateScheduleRequest struct {
	JobID          string  `json:"jobId"`
	Name           string  `json:"name"`
	Kind           string  `json:"kind"`
	CronExpression *string `json:"cronExpression"`
	EventName      *string `json:"eventName"`
}

type UpdateScheduleRequest struct {
	Name           *string `json:"name,omitempty"`
	CronExpression *string `json:"cronExpression,omitempty"`
	EventName      *string `json:"eventName,omitempty"`
	Enabled        *bool   `json:"enabled,omitempty"`
}

type Schedule struct {
	ID             string  `json:"id"`
	ProjectID      string  `json:"projectId"`
	JobID          string  `json:"jobId"`
	Name           string  `json:"name"`
	Kind           string  `json:"kind"`
	Enabled        bool    `json:"enabled"`
	CronExpression *string `json:"cronExpression"`
	EventName      *string `json:"eventName"`
	NextRunAt      *string `json:"nextRunAt"`
	LastFiredAt    *string `json:"lastFiredAt"`
	CreatedAt      string  `json:"createdAt"`
}

func (c *Client) GetSchedule(ctx context.Context, id string) (*Schedule, error) {
	var schedule Schedule
	if err := c.do(ctx, http.MethodGet, "/api/v1/schedules/"+escape(id), nil, &schedule); err != nil {
		return nil, err
	}
	return &schedule, nil
}

func (c *Client) CreateSchedule(ctx context.Context, projectID string, request CreateScheduleRequest) (*Schedule, error) {
	var schedule Schedule
	err := c.do(ctx, http.MethodPost, "/api/v1/projects/"+escape(projectID)+"/schedules", request, &schedule)
	if err != nil {
		return nil, err
	}
	return &schedule, nil
}

func (c *Client) UpdateSchedule(ctx context.Context, id string, request UpdateScheduleRequest) (*Schedule, error) {
	var schedule Schedule
	err := c.do(ctx, http.MethodPut, "/api/v1/schedules/"+escape(id), request, &schedule)
	if err != nil {
		return nil, err
	}
	return &schedule, nil
}

func (c *Client) DeleteSchedule(ctx context.Context, id string) error {
	return c.do(ctx, http.MethodDelete, "/api/v1/schedules/"+escape(id), nil, nil)
}
