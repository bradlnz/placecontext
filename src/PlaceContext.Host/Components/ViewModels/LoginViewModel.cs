namespace PlaceContext.Host.Components.ViewModels;

public sealed class LoginViewModel : PageViewModel
{
    public string? Error { get; private set; }
    public string? ReturnUrl { get; private set; }
    public string? Email { get; private set; }

    public void SetParameters(string? error, string? returnUrl, string? email)
    {
        Error = error;
        ReturnUrl = returnUrl;
        Email = email;
    }
}
