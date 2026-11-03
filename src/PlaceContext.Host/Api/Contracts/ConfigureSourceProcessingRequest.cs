namespace PlaceContext.Host.Api;

public sealed record ConfigureSourceProcessingRequest(bool Enabled, string Criteria, double MinimumConfidence = 0.9, bool ProcessingApproved = false);
