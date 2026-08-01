using System.Security.Claims;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Sign-in, sign-out, first-run setup, and invite acceptance.
///
/// Password login (added here): a fresh tenant is "unconfigured" until an operator completes /setup
/// (email + strong password → the tenant's Owner); from then on /login verifies email + password. In
/// Development, /locked keeps auto-signing the operator in with no password (unchanged — the team's
/// local workflow and the Playwright/verify harness rely on it); everywhere else /locked now redirects
/// to /setup or /login instead of signing anyone in. The pctl TUI's HMAC-token machine path
/// (/auth/portal) is untouched — it never counts towards "configured" (see IAuthService.GetOrCreateOperatorAsync),
/// so it can keep bootstrapping a headless session without ever needing a password. /join turns an
/// invite token into a member. Login and setup actions are anonymous; 2FA management endpoints require authentication.
/// </summary>
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly PortalToken _portal;
    private readonly IMembershipService _members;
    private readonly IAntiforgery _antiforgery;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly IDataEncryptor _encryptor;

    public AuthController(IAuthService auth, PortalToken portal, IMembershipService members,
        IAntiforgery antiforgery, IConfiguration config, IWebHostEnvironment env, IDataEncryptor encryptor)
    {
        _auth = auth;
        _portal = portal;
        _members = members;
        _antiforgery = antiforgery;
        _config = config;
        _env = env;
        _encryptor = encryptor;
    }

    // Token sign-in (self-hosted; the pctl TUI mints the token and opens /auth/portal).
    // The portal has no password login. A valid short-lived token (HMAC-signed with the shared
    // PlaceContext:Portal:SigningKey) signs the cluster operator into the cookie. In Development with no
    // key configured, sign-in is automatic so `./run.sh` + opening localhost just works with no cluster.
    [HttpGet("/auth/portal")]
    [AllowAnonymous]
    public async Task<IActionResult> Portal(string? token, string? returnUrl)
    {
        var portalSigningKey = _config["PlaceContext:Portal:SigningKey"];
        var devAutoLogin = _env.IsDevelopment() && string.IsNullOrWhiteSpace(portalSigningKey);

        if (!devAutoLogin && !_portal.TryValidate(token, portalSigningKey, DateTimeOffset.UtcNow))
            return Redirect("/locked");
        var operatorUser = await _auth.GetOrCreateOperatorAsync(HttpContext.RequestAborted);
        await SignInAsync(HttpContext, operatorUser);
        return Redirect(LocalOrHome(returnUrl));
    }

    // The cookie scheme's LoginPath: every unauthenticated request to a protected page lands here.
    //   • Development: unchanged — auto-signs in as the operator with no password, exactly as before, so
    //     local dev and the Playwright/verify harness keep working with zero setup.
    //   • Everywhere else: no more password-less auto-login. Send the operator to /setup (tenant has no
    //     admin with a real password yet) or /login (it does), carrying the return URL through either way.
    [HttpGet("/locked")]
    [AllowAnonymous]
    public async Task<IActionResult> Locked()
    {
        if (_env.IsDevelopment())
        {
            var operatorUser = await _auth.GetOrCreateOperatorAsync(HttpContext.RequestAborted);
            await SignInAsync(HttpContext, operatorUser);
            // Honour the cookie middleware's ReturnUrl so deep links survive the auto-login round trip.
            return Redirect(LocalOrHome(Request.Query["ReturnUrl"]));
        }

        var returnUrl = LocalOrHome(Request.Query["ReturnUrl"]);
        var target = await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted) ? "/setup" : "/login";
        return Redirect(returnUrl == "/" ? target : $"{target}?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    // First-run setup: creates this tenant's Owner (email + strong password) and signs them straight in.
    // Fails closed — refuses (redirects to /login) once an admin already exists, so this can never be
    // replayed into a second admin. Rendered by the Setup.razor page; posted as a plain HTML form (not a
    // Blazor circuit) so the response can set the auth cookie directly, same as every other sign-in path
    // on this controller.
    [HttpPost("/auth/setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup([FromForm] string email, [FromForm] string? displayName,
        [FromForm] string password, [FromForm] string confirmPassword)
    {
        if (!await ValidAntiforgeryAsync())
            return Redirect("/setup?error=" + Uri.EscapeDataString("Your session expired — please try again."));

        if (!await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted))
            return Redirect("/login"); // someone already finished setup — this page is a dead end now

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Redirect("/setup?error=" + Uri.EscapeDataString("Enter a valid email address."));

        // Defense in depth: the Setup page enforces this client-side too, but the server never trusts that.
        var policyError = PasswordPolicy.Validate(password, confirmPassword);
        if (policyError is not null)
            return Redirect("/setup?error=" + Uri.EscapeDataString(policyError));

        var admin = await _auth.CreateFirstAdminAsync(email, displayName ?? "", password, HttpContext.RequestAborted);
        if (admin is null)
            return Redirect("/login"); // lost a race to a concurrent /setup submission

        await SignInAsync(HttpContext, admin);
        return Redirect("/");
    }

    /// <summary>
    /// Whether first-run admin setup is still needed. Used by the install CLI/TUI to decide whether
    /// to prompt for the default admin account.
    /// </summary>
    [HttpGet("/auth/setup/status")]
    [AllowAnonymous]
    public async Task<IActionResult> SetupStatus()
    {
        var unconfigured = await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted);
        return Ok(new { configured = !unconfigured, needsAdmin = unconfigured });
    }

    /// <summary>
    /// Non-browser first-run setup (install CLI / Terraform). JSON only; no antiforgery (there is no
    /// form session). Fails closed once any Owner with a real password exists — same as the form path.
    /// Does not set a cookie (install is headless); the operator signs in at /login afterward.
    /// </summary>
    [HttpPost("/auth/setup/cli")]
    [AllowAnonymous]
    public async Task<IActionResult> SetupCli([FromBody] SetupCliRequest? body)
    {
        if (!await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted))
            return Conflict(new { error = "Admin already configured." });

        if (body is null || string.IsNullOrWhiteSpace(body.Email) || !body.Email.Contains('@'))
            return BadRequest(new { error = "Enter a valid email address." });

        var policyError = PasswordPolicy.Validate(body.Password, body.ConfirmPassword ?? body.Password);
        if (policyError is not null)
            return BadRequest(new { error = policyError });

        var admin = await _auth.CreateFirstAdminAsync(
            body.Email, body.DisplayName ?? "", body.Password ?? "", HttpContext.RequestAborted);
        if (admin is null)
            return Conflict(new { error = "Could not create admin (already configured or email taken)." });

        return Ok(new { email = admin.Email, displayName = admin.DisplayName, role = admin.Role.ToString() });
    }

    public sealed record SetupCliRequest(string Email, string? DisplayName, string Password, string? ConfirmPassword);

    // Password login. Always shows the same generic error on failure (unknown email, wrong password, or a
    // member with no password set) so a response can't be used to enumerate accounts, and applies a small
    // fixed delay on failure (see LoginThrottle) so a naive brute-force loop can't run at line rate.
    [HttpPost("/auth/login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] string? returnUrl)
    {
        if (!await ValidAntiforgeryAsync())
            return Redirect("/login?error=" + Uri.EscapeDataString("Your session expired — please try again."));

        var user = await _auth.ValidateCredentialsAsync(email ?? "", password ?? "", HttpContext.RequestAborted);
        if (user is null)
        {
            await LoginThrottle.PenalizeAsync(email ?? "");
            return Redirect(BuildLoginErrorUrl("Invalid email or password.", returnUrl, email));
        }

        LoginThrottle.Clear(email ?? "");

        if (await _auth.IsTwoFactorRequiredAsync(HttpContext.RequestAborted))
        {
            var delivery = await _auth.GetTwoFactorDeliveryInfoAsync(
                user.Id, channel: null, HttpContext.RequestAborted);
            var channel = delivery.Channel;
            var destination = delivery.MaskedDestination;
            if (!delivery.RequiresPhoneEnrollment)
            {
                try
                {
                    var challenge = await _auth.IssueTwoFactorCodeAsync(
                        user.Id, channel: null, HttpContext.RequestAborted);
                    channel = challenge.Channel;
                    destination = challenge.MaskedDestination;
                }
                catch (Exception)
                {
                    return Redirect(BuildLoginErrorUrl(
                        "We could not send a verification code. Ask an owner to check Settings → Communications.",
                        returnUrl, email));
                }
            }

            var stateToken = ProtectEmailState(new EmailTwoFactorState
            {
                UserId = user.Id, TenantId = user.TenantId, Email = user.Email,
                DisplayName = user.DisplayName, Role = user.Role.ToString(),
                ReturnUrl = LocalOrHome(returnUrl),
                Channel = channel,
                Destination = destination,
                NeedsPhone = delivery.RequiresPhoneEnrollment,
                EmailAvailable = delivery.EmailAvailable,
                SmsAvailable = delivery.SmsAvailable,
            });
            var qs = $"state={Uri.EscapeDataString(stateToken)}";
            return Redirect($"/auth/email-verify?{qs}");
        }

        await SignInAsync(HttpContext, user);
        return Redirect(LocalOrHome(returnUrl));
    }

    private async Task<bool> ValidAntiforgeryAsync()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    // Carries the entered email (not the password) back onto the login form so a mistyped password
    // doesn't also force retyping the email — an email address in a redirect URL isn't sensitive the
    // way a password would be.
    private static string BuildLoginErrorUrl(string error, string? returnUrl, string? email)
    {
        var url = "/login?error=" + Uri.EscapeDataString(error);
        if (!string.IsNullOrEmpty(returnUrl))
            url += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
        if (!string.IsNullOrEmpty(email))
            url += "&email=" + Uri.EscapeDataString(email);
        return url;
    }

    // Invite acceptance (join page).
    [HttpGet("/join")]
    [AllowAnonymous]
    public async Task<IActionResult> Join(string? token, string? error)
    {
        var info = string.IsNullOrEmpty(token) ? null : await _members.GetInviteAsync(token, HttpContext.RequestAborted);
        if (info is null)
            return Content(AuthPages.JoinInvalid(), "text/html");
        return Content(AuthPages.Join(_antiforgery.GetAndStoreTokens(HttpContext), token!, info, error), "text/html");
    }

    [HttpPost("/auth/accept-invite")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvite([FromForm] string token, [FromForm] string password, [FromForm] string? displayName)
    {
        if (!await ValidAntiforgeryAsync())
            return Redirect($"/join?token={Uri.EscapeDataString(token ?? "")}&error=" + Uri.EscapeDataString("Your session expired — please try again."));

        var policyError = PasswordPolicy.Validate(password);
        if (policyError is not null)
            return Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString(policyError));

        AuthUser? user;
        try
        {
            user = await _members.AcceptInviteAsync(token, displayName ?? "", password!, HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString(ex.Message));
        }
        if (user is null)
            return Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString("This invite is invalid, used, expired, or the email is already a member."));
        await SignInAsync(HttpContext, user);
        return Redirect("/");
    }

    [HttpPost("/auth/logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        if (!await ValidAntiforgeryAsync()) return BadRequest();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/locked");
    }

    // Signs the user into the cookie scheme with their identity + tenant + role claims.
    private static Task SignInAsync(HttpContext ctx, AuthUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("tenant", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("role", user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    // Guards against open redirects: only a same-site relative path (e.g. "/connect/authorize?…") is
    // honoured as a post-login destination; anything absolute or protocol-relative falls back to home.
    private static string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
            && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
                ? returnUrl : "/";

    // ── Login 2FA (verification code by email or SMS) ──────────────────────────────────────────

    [HttpGet("/auth/email-verify")]
    [AllowAnonymous]
    public IActionResult EmailVerify([FromQuery] string? state, [FromQuery] string? error)
    {
        if (string.IsNullOrEmpty(state)) return Redirect("/login");
        var pending = DecryptEmailState(state);
        if (pending is null) return Redirect("/login");
        return RenderVerify(pending, state, error);
    }

    [HttpPost("/auth/email-verify")]
    [AllowAnonymous]
    public async Task<IActionResult> EmailVerifyPost(
        [FromForm] string state, [FromForm] string? code, [FromForm] string? phone)
    {
        if (!await ValidAntiforgeryAsync() || string.IsNullOrEmpty(state))
            return Redirect("/login");

        var pending = DecryptEmailState(state);
        if (pending is null) return Redirect("/login");

        // Phone enrollment: the code goes by SMS but no number is on file yet — collect it first,
        // then send the code to the freshly stored number.
        if (pending.NeedsPhone || !string.IsNullOrWhiteSpace(phone))
        {
            if (string.IsNullOrWhiteSpace(phone))
                return RenderVerify(pending, state, "Enter your mobile number so we can text you the code.");
            try
            {
                await _auth.SetTwoFactorPhoneNumberAsync(
                    pending.UserId, phone, HttpContext.RequestAborted);
            }
            catch (ArgumentException ex)
            {
                return RenderVerify(pending, state, ex.Message);
            }

            try
            {
                var challenge = await _auth.IssueTwoFactorCodeAsync(
                    pending.UserId, "sms", HttpContext.RequestAborted);
                pending.Channel = challenge.Channel;
                pending.Destination = challenge.MaskedDestination;
                pending.NeedsPhone = false;
            }
            catch (Exception)
            {
                return RenderVerify(pending, state,
                    "We could not send the code. Please try again shortly.");
            }
            var enrolled = ProtectEmailState(pending);
            return Redirect($"/auth/email-verify?state={Uri.EscapeDataString(enrolled)}");
        }

        var ok = await _auth.VerifyTwoFactorCodeAsync(
            pending.UserId, code ?? "", HttpContext.RequestAborted);
        if (!ok)
        {
            var qs = $"state={Uri.EscapeDataString(state)}&error="
                + Uri.EscapeDataString("Invalid or expired code — request another code if needed.");
            return Redirect($"/auth/email-verify?{qs}");
        }

        var user = new AuthUser(pending.UserId, pending.TenantId, pending.Email,
            pending.DisplayName, pending.Role);
        await SignInAsync(HttpContext, user);
        return Redirect(LocalOrHome(pending.ReturnUrl));
    }

    [HttpPost("/auth/email-verify/resend")]
    [AllowAnonymous]
    public async Task<IActionResult> EmailVerifyResend([FromForm] string state)
    {
        if (!await ValidAntiforgeryAsync()) return Redirect("/login");
        var pending = DecryptEmailState(state);
        if (pending is null) return Redirect("/login");
        if (!await _auth.IsTwoFactorRequiredAsync(HttpContext.RequestAborted))
            return Redirect("/login");
        if (pending.NeedsPhone)
            return Redirect($"/auth/email-verify?state={Uri.EscapeDataString(state)}");
        try
        {
            var challenge = await _auth.IssueTwoFactorCodeAsync(
                pending.UserId, pending.Channel, HttpContext.RequestAborted);
            pending.Channel = challenge.Channel;
            pending.Destination = challenge.MaskedDestination;
            var resent = ProtectEmailState(pending);
            return Redirect($"/auth/email-verify?state={Uri.EscapeDataString(resent)}");
        }
        catch (InvalidOperationException ex)
        {
            var qs = $"state={Uri.EscapeDataString(state)}&error={Uri.EscapeDataString(ex.Message)}";
            return Redirect($"/auth/email-verify?{qs}");
        }
        catch (Exception)
        {
            var qs = $"state={Uri.EscapeDataString(state)}&error="
                + Uri.EscapeDataString("We could not send a new code. Please try again shortly.");
            return Redirect($"/auth/email-verify?{qs}");
        }
    }

    // "Send via SMS/email instead" — re-issues the code on the other channel (offered only when both
    // channels have a 2FA-flagged provider). The usual one-minute resend cooldown still applies.
    [HttpGet("/auth/email-verify/channel")]
    [AllowAnonymous]
    public async Task<IActionResult> EmailVerifyChannel(
        [FromQuery] string? state, [FromQuery] string? channel)
    {
        if (string.IsNullOrEmpty(state)) return Redirect("/login");
        var pending = DecryptEmailState(state);
        if (pending is null || channel is not ("email" or "sms")) return Redirect("/login");
        if (!(pending.EmailAvailable && pending.SmsAvailable))
            return Redirect($"/auth/email-verify?state={Uri.EscapeDataString(state)}");

        string? error = null;
        var delivery = await _auth.GetTwoFactorDeliveryInfoAsync(
            pending.UserId, channel, HttpContext.RequestAborted);
        pending.Channel = delivery.Channel;
        pending.Destination = delivery.MaskedDestination;
        pending.NeedsPhone = delivery.RequiresPhoneEnrollment;
        if (!delivery.RequiresPhoneEnrollment)
        {
            try
            {
                var challenge = await _auth.IssueTwoFactorCodeAsync(
                    pending.UserId, channel, HttpContext.RequestAborted);
                pending.Channel = challenge.Channel;
                pending.Destination = challenge.MaskedDestination;
            }
            catch (InvalidOperationException ex) { error = ex.Message; }
            catch (Exception)
            {
                error = "We could not send a new code. Please try again shortly.";
            }
        }

        var switched = ProtectEmailState(pending);
        var qs = $"state={Uri.EscapeDataString(switched)}";
        if (error is not null) qs += $"&error={Uri.EscapeDataString(error)}";
        return Redirect($"/auth/email-verify?{qs}");
    }

    private IActionResult RenderVerify(EmailTwoFactorState pending, string state, string? error)
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        string? switchHref = null;
        string? switchLabel = null;
        if (pending is { EmailAvailable: true, SmsAvailable: true, NeedsPhone: false })
        {
            var other = pending.Channel == "sms" ? "email" : "sms";
            switchHref = $"/auth/email-verify/channel?state={Uri.EscapeDataString(state)}&channel={other}";
            switchLabel = other == "sms"
                ? "Send the code by SMS instead"
                : "Send the code to your email instead";
        }

        var destination = WebUtility.HtmlEncode(pending.Destination);
        var model = pending.NeedsPhone
            ? new AuthPages.EmailVerifyModel(
                "Add your phone number",
                "Verification codes are delivered by SMS. Enter your mobile number and we'll text you a code.",
                RequiresPhone: true, switchHref, switchLabel)
            : pending.Channel == "sms"
                ? new AuthPages.EmailVerifyModel(
                    "Check your phone",
                    $"Enter the 6-digit verification code sent by SMS to <strong>{destination}</strong>.",
                    RequiresPhone: false, switchHref, switchLabel)
                : new AuthPages.EmailVerifyModel(
                    "Check your email",
                    $"Enter the 6-digit verification code sent to <strong>{destination}</strong>.",
                    RequiresPhone: false, switchHref, switchLabel);
        return Content(AuthPages.EmailVerify(tokens, state, model, error), "text/html");
    }

    private string ProtectEmailState(EmailTwoFactorState state)
        => _encryptor.Protect(
            JsonSerializer.Serialize(state), IDataEncryptor.Purpose.EmailTwoFactorState);

    private EmailTwoFactorState? DecryptEmailState(string state)
    {
        try
        {
            var json = _encryptor.Unprotect(state, IDataEncryptor.Purpose.EmailTwoFactorState);
            return string.IsNullOrEmpty(json)
                ? null
                : JsonSerializer.Deserialize<EmailTwoFactorState>(json);
        }
        catch { return null; }
    }

    private sealed class EmailTwoFactorState
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "";
        public string? ReturnUrl { get; set; }
        /// <summary>Channel the code was (or will be) delivered on: "email" | "sms".</summary>
        public string Channel { get; set; } = "email";
        /// <summary>Masked delivery target shown on the page (masked email or phone).</summary>
        public string Destination { get; set; } = "";
        /// <summary>True when SMS delivery is required but no phone number is on file yet.</summary>
        public bool NeedsPhone { get; set; }
        public bool EmailAvailable { get; set; }
        public bool SmsAvailable { get; set; }
    }

    // ── 2FA Management API ─────────────────────────────────────────────────────────────────────
    // 2FA is org-wide: mandatory for everyone once any communication provider is flagged
    // UseForTwoFactor (Settings → Communications). These self-service endpoints only manage the
    // user's own delivery details — phone number and preferred channel.

    [HttpGet("/api/2fa/status")]
    [Authorize]
    public async Task<IActionResult> TwoFactorStatus()
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var info = await _auth.GetTwoFactorSettingsAsync(userId.Value, HttpContext.RequestAborted);
        return Ok(new
        {
            required = info.Required,
            channel = info.PreferredChannel,
            phoneNumber = info.PhoneNumber,
            emailAvailable = info.EmailAvailable,
            smsAvailable = info.SmsAvailable,
        });
    }

    [HttpPost("/api/2fa/phone")]
    [Authorize]
    public async Task<IActionResult> TwoFactorPhone([FromBody] TwoFactorPhoneRequest? body)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        try
        {
            await _auth.SetTwoFactorPhoneNumberAsync(
                userId.Value, body?.PhoneNumber, HttpContext.RequestAborted);
            return Ok(new { saved = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("/api/2fa/channel")]
    [Authorize]
    public async Task<IActionResult> TwoFactorChannel([FromBody] TwoFactorChannelRequest? body)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (body?.Channel is null) return BadRequest(new { error = "Channel required." });
        try
        {
            await _auth.SetTwoFactorChannelAsync(userId.Value, body.Channel, HttpContext.RequestAborted);
            return Ok(new { saved = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }

    public sealed record TwoFactorPhoneRequest(string? PhoneNumber);
    public sealed record TwoFactorChannelRequest(string Channel);
}
