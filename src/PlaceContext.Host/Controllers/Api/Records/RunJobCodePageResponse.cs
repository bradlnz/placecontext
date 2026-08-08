using PlaceContext.Jobs.Contracts.Management;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record RunJobCodePageResponse(JobResponse Job, JobRunDetailPageResponse Run);
