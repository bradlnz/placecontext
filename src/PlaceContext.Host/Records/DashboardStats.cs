namespace PlaceContext.Host.Controllers;

public sealed record DashboardStats(
    int Running,
    int Queued,
    int Failed24,
    int Succeeded24);
