package client

// FileEntry is one file of a multi-file inline code source (map or reduce step).
type FileEntry struct {
	Path    string `json:"path"`
	Content string `json:"content"`
}

// JobParameter is an input prompted before a manual run of a job.
type JobParameter struct {
	Name     string   `json:"name"`
	Label    *string  `json:"label,omitempty"`
	Required bool     `json:"required"`
	Type     string   `json:"type"`
	Options  []string `json:"options,omitempty"`
}

// Project mirrors the API project DTO. Only id/name/path are user-managed; the
// rest are server-computed and read-only.
type Project struct {
	ID           string `json:"id,omitempty"`
	Name         string `json:"name,omitempty"`
	Path         string `json:"path,omitempty"`
	Status       string `json:"status,omitempty"`
	IsGraphified bool   `json:"isGraphified"`
}

// CreateProjectRequest is the body for POST /api/v1/projects.
type CreateProjectRequest struct {
	Path string `json:"path"`
	Name string `json:"name,omitempty"`
}

// Job mirrors the API job DTO used for reads and as the create/update request body.
// mapSourceKind / reduceSourceKind / createdAt / updatedAt are server-computed on read.
type Job struct {
	ID          string  `json:"id,omitempty"`
	ProjectID   string  `json:"projectId,omitempty"`
	Name        string  `json:"name"`
	Description *string `json:"description,omitempty"`

	MapSourceKind *string     `json:"mapSourceKind,omitempty"`
	MapImage      *string     `json:"mapImage,omitempty"`
	MapRuntimeID  *string     `json:"mapRuntimeId,omitempty"`
	MapSource     *string     `json:"mapSource,omitempty"`
	MapEntrypoint *string     `json:"mapEntrypoint,omitempty"`
	MapFiles      []FileEntry `json:"mapFiles,omitempty"`

	InputPayloads []string          `json:"inputPayloads,omitempty"`
	MapEnv        map[string]string `json:"mapEnv,omitempty"`

	ReduceSourceKind *string           `json:"reduceSourceKind,omitempty"`
	ReduceImage      *string           `json:"reduceImage,omitempty"`
	ReduceRuntimeID  *string           `json:"reduceRuntimeId,omitempty"`
	ReduceSource     *string           `json:"reduceSource,omitempty"`
	ReduceEntrypoint *string           `json:"reduceEntrypoint,omitempty"`
	ReduceFiles      []FileEntry       `json:"reduceFiles,omitempty"`
	ReduceEnv        map[string]string `json:"reduceEnv,omitempty"`

	ConcurrencyLimit   int            `json:"concurrencyLimit,omitempty"`
	SuccessExitCodes   []int          `json:"successExitCodes,omitempty"`
	PartialExitCodes   []int          `json:"partialExitCodes,omitempty"`
	AllowNetworkEgress bool           `json:"allowNetworkEgress"`
	Parameters         []JobParameter `json:"parameters,omitempty"`
	PostJobActions     []string       `json:"postJobActions,omitempty"`
	ReturnType         string         `json:"returnType,omitempty"`
	ReturnFileName     *string        `json:"returnFileName,omitempty"`

	CreatedAt string `json:"createdAt,omitempty"`
	UpdatedAt string `json:"updatedAt,omitempty"`
}

// Schedule mirrors the API schedule DTO.
type Schedule struct {
	ID             string  `json:"id,omitempty"`
	ProjectID      string  `json:"projectId,omitempty"`
	JobID          string  `json:"jobId,omitempty"`
	Name           string  `json:"name,omitempty"`
	Kind           string  `json:"kind,omitempty"`
	Enabled        bool    `json:"enabled"`
	CronExpression *string `json:"cronExpression,omitempty"`
	EventName      *string `json:"eventName,omitempty"`
	NextRunAt      *string `json:"nextRunAt,omitempty"`
	LastFiredAt    *string `json:"lastFiredAt,omitempty"`
	CreatedAt      string  `json:"createdAt,omitempty"`
}

// CreateScheduleRequest is the body for POST /api/v1/projects/{projectId}/schedules.
type CreateScheduleRequest struct {
	JobID          string  `json:"jobId"`
	Name           string  `json:"name"`
	Kind           string  `json:"kind"`
	CronExpression *string `json:"cronExpression,omitempty"`
	EventName      *string `json:"eventName,omitempty"`
}

// UpdateScheduleRequest is the only supported mutation on an existing schedule.
type UpdateScheduleRequest struct {
	Enabled bool `json:"enabled"`
}
