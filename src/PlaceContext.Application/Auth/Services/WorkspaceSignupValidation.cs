using System.Net.Mail;
using System.Text.RegularExpressions;
using PlaceContext.Application.Billing;

namespace PlaceContext.Application.Auth;

public static class WorkspaceSignupValidation
{
    private static readonly Regex SlugRule = new(
        "^[a-z][a-z0-9-]{2,38}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "www", "api", "app", "auth", "admin", "billing", "login",
        "signup", "register", "setup", "support", "help", "docs", "status",
        "mcp", "static", "assets", "localhost", "system", "internal",
        "security", "mail", "smtp", "root", "demo"
    };

    public static string? ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)
            || !SlugRule.IsMatch(slug)
            || Reserved.Contains(slug)
            || slug.Contains("--", StringComparison.Ordinal))
        {
            return "Use 4-40 lowercase letters, numbers or hyphens; start with a letter and end with a letter or number.";
        }
        return null;
    }

    public static string? ValidateSignup(WorkspaceSignupInput input)
    {
        if (string.IsNullOrWhiteSpace(input.WorkspaceName)
            || input.WorkspaceName.Trim().Length is < 2 or > 90)
        {
            return "Enter a workspace name between 2 and 90 characters.";
        }
        var slugError = ValidateSlug(input.Slug);
        if (slugError is not null)
        {
            return slugError;
        }
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254
                    || !MailAddress.TryCreate(input.Email, out var parsed)
                    || !string.Equals(parsed.Address, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Enter a valid work email.";
        }
        if (!string.IsNullOrWhiteSpace(input.DisplayName) && input.DisplayName.Trim().Length > 120)
        {
            return "Your name must be shorter than 120 characters.";
        }
        if (input.Plan is not (RunPlans.Basic or RunPlans.Standard))
        {
            return "Choose Basic or Standard Managed.";
        }
        return PasswordPolicy.Validate(input.Password, input.ConfirmPassword);
    }

}
