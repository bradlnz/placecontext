using PlaceContext.Host.Api;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record RunJobCodePageResponse(JobResponse Job, JobRunDetailPageResponse Run);
