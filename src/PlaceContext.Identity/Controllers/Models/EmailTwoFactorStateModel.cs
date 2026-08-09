namespace PlaceContext.Identity.Controllers;

internal sealed class EmailTwoFactorStateModel
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
    public string Channel { get; set; } = "email";
    public string Destination { get; set; } = string.Empty;
    public bool NeedsPhone { get; set; }
    public bool EmailAvailable { get; set; }
    public bool SmsAvailable { get; set; }
}
