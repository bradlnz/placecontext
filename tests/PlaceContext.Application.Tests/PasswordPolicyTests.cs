using PlaceContext.Application.Auth;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>Unit tests for the pure first-run/admin password-strength policy. No I/O involved — the
/// same rules run both client-facing (the Setup page's UX check) and server-side (AuthController /
/// AuthService, as defense in depth).</summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("short1234")]      // under the 12-char minimum
    [InlineData("")]               // empty
    [InlineData("password1234")]   // common/denylisted despite meeting the length bar
    [InlineData("aaaaaaaaaaaa")]   // low-variety (single repeated character)
    [InlineData("abababababab")]   // low-variety (two alternating characters)
    [InlineData("abcdefghijkl")]   // sequential run
    [InlineData("123456789012")]   // sequential run (digits)
    public void Rejects_short_or_weak_passwords(string candidate)
    {
        var error = PasswordPolicy.Validate(candidate);

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Tr0ub4dor&3xyz!")]
    [InlineData("correct-battery-staple-9")]
    [InlineData("Zx7!qLmP4#vRw2")]
    public void Accepts_strong_passwords(string candidate)
    {
        var error = PasswordPolicy.Validate(candidate);

        Assert.Null(error);
    }

    [Fact]
    public void Rejects_a_strong_password_whose_confirmation_does_not_match()
    {
        var error = PasswordPolicy.Validate("Zx7!qLmP4#vRw2", "Zx7!qLmP4#vRw2different");

        Assert.NotNull(error);
    }

    [Fact]
    public void Accepts_a_strong_password_whose_confirmation_matches()
    {
        var error = PasswordPolicy.Validate("Zx7!qLmP4#vRw2", "Zx7!qLmP4#vRw2");

        Assert.Null(error);
    }

    [Fact]
    public void Confirmation_is_optional_for_defense_in_depth_callers()
    {
        // AuthService re-validates with only the password (no confirmation available server-side once
        // the form has already matched it) — Validate must not require a confirmation to be supplied.
        var error = PasswordPolicy.Validate("Zx7!qLmP4#vRw2");

        Assert.Null(error);
    }
}
