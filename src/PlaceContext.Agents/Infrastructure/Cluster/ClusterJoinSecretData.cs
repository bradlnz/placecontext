namespace PlaceContext.Agents.Infrastructure.Cluster;

internal sealed record ClusterJoinSecretData(
    string Token,
    string? ServerUrl,
    string? TailscaleAuthKey);
