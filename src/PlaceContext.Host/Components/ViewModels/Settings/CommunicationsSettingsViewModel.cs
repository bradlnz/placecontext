using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Host.Components.ViewModels;

public enum CommunicationChannel
{
    Email,
    Sms,
}

public enum CommunicationProviderKind
{
    Postmark,
    SendGrid,
    Twilio,
}

public sealed record CommunicationKindOption(
    CommunicationProviderKind Kind,
    CommunicationChannel Channel
);

public sealed class CommunicationsSettingsViewModel(
    IServiceScopeFactory ScopeFactory,
    PortalUiState Ui
) : PageViewModel
{
    public const string EmailChannel = "email";
    public const string SmsChannel = "sms";
    public const string PostmarkKind = "postmark";
    public const string SendGridKind = "sendgrid";
    public const string TwilioKind = "twilio";
    public const string NoneAuth = "none";
    public const string BearerAuth = "bearer";
    public const string HeaderAuth = "header";
    public const string BasicAuth = "basic";
    public const string DefaultMessageStream = "outbound";

    public IReadOnlyList<CommunicationProviderView> Providers =
        Array.Empty<CommunicationProviderView>();
    public IReadOnlyList<ProjectSummaryView> Projects = Array.Empty<ProjectSummaryView>();
    public IReadOnlyList<ProjectSecretView> Secrets = Array.Empty<ProjectSecretView>();
    public bool Loading = true;
    public string? LoadError;
    public string? Message;
    public bool MessageIsError;
    public Guid? ConfirmDeleteId;
    public bool ShowForm;

    public bool Saving;
    public Guid? EditingId;
    public string Channel = EmailChannel;
    public string Kind = PostmarkKind;
    public string Name = "";
    public bool Enabled = true;
    public string AuthType = NoneAuth;
    public string AuthHeaderName = "";
    public Guid? VaultProjectId;
    public string SecretName = "";
    public string FromEmail = "";
    public string FromName = "";
    public string MessageStream = DefaultMessageStream;
    public string AccountSid = "";
    public string FromNumber = "";
    public string Endpoint = "";
    public bool IsHeaderAuth => AuthType == HeaderAuth;
    public bool UsesSecret => RequiresSecret(AuthType);
    public bool IsBasicAuth => AuthType == BasicAuth;
    public bool IsTransactionalKind => Kind is PostmarkKind or SendGridKind;
    public bool IsPostmark => Kind == PostmarkKind;
    public bool IsTwilio => Kind == TwilioKind;

    public string TestRecipientPlaceholder(string channel) =>
        channel == EmailChannel ? "recipient@example.com" : "+15551234567";

    public void CloseForm() => ShowForm = false;

    // Test-send state.
    public Guid? TestProviderId;
    public string TestRecipient = "";
    public bool TestSending;

    public async Task LoadAsync()
    {
        Ui.Set("Settings", "Communications");
        Loading = true;
        LoadError = null;
        try
        {
            Providers = await InScopeAsync<
                CommunicationProviderService,
                IReadOnlyList<CommunicationProviderView>
            >(service => service.ListAsync());
            Projects = await InScopeAsync<IPlaceContextService, IReadOnlyList<ProjectSummaryView>>(
                service => service.GetProjectsAsync()
            );
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    public async Task ReloadAsync() =>
        Providers = await InScopeAsync<
            CommunicationProviderService,
            IReadOnlyList<CommunicationProviderView>
        >(service => service.ListAsync());

    public void Report(string message, bool isError = false) =>
        (Message, MessageIsError) = (message, isError);

    public string[] KindsForChannel(string channel) =>
        channel == SmsChannel ? [TwilioKind] : [PostmarkKind, SendGridKind];

    public string KindLabel(string kind) =>
        kind switch
        {
            PostmarkKind => "Postmark",
            SendGridKind => "SendGrid",
            TwilioKind => "Twilio",
            _ => kind,
        };

    public string ChannelLabel(string channel) => channel == SmsChannel ? "SMS" : "Email";

    public static string DefaultHeaderName(string kind) =>
        kind == PostmarkKind ? "X-Postmark-Server-Token" : "";

    public void ChannelChanged(ChangeEventArgs args)
    {
        Channel = args.Value?.ToString() ?? EmailChannel;
        if (!KindsForChannel(Channel).Contains(Kind))
            Kind = KindsForChannel(Channel)[0];
        if (AuthType == "header")
            AuthHeaderName = DefaultHeaderName(Kind);
    }

    public void KindChanged(ChangeEventArgs args)
    {
        Kind = args.Value?.ToString() ?? PostmarkKind;
        if (AuthType == "header")
            AuthHeaderName = DefaultHeaderName(Kind);
    }

    public void AuthTypeChanged(ChangeEventArgs args)
    {
        AuthType = args.Value?.ToString() ?? NoneAuth;
        if (AuthType == "header" && string.IsNullOrWhiteSpace(AuthHeaderName))
            AuthHeaderName = DefaultHeaderName(Kind);
    }

    public async Task ProjectChanged(ChangeEventArgs args)
    {
        VaultProjectId = Guid.TryParse(args.Value?.ToString(), out var id) ? id : null;
        SecretName = "";
        await LoadSecretsAsync();
    }

    public async Task LoadSecretsAsync()
    {
        Secrets = VaultProjectId is { } id
            ? await InScopeAsync<IPlaceContextService, IReadOnlyList<ProjectSecretView>>(service =>
                service.ListProjectSecretsAsync(id)
            )
            : Array.Empty<ProjectSecretView>();
    }

    public void ResetForm()
    {
        EditingId = null;
        Channel = "email";
        Kind = "postmark";
        Name = "";
        Enabled = true;
        AuthType = "none";
        AuthHeaderName = "";
        VaultProjectId = null;
        SecretName = "";
        Secrets = Array.Empty<ProjectSecretView>();
        FromEmail = "";
        FromName = "";
        MessageStream = "outbound";
        AccountSid = "";
        FromNumber = "";
        Endpoint = "";
    }

    public void ShowAdd()
    {
        ResetForm();
        ShowForm = true;
    }

    public async Task StartEdit(CommunicationProviderView provider)
    {
        ResetForm();
        EditingId = provider.Id;
        Channel = provider.Channel;
        Kind = provider.Kind;
        Name = provider.Name;
        Enabled = provider.Enabled;
        AuthType = provider.AuthType;
        AuthHeaderName = provider.AuthHeaderName ?? DefaultHeaderName(provider.Kind);
        VaultProjectId = provider.VaultProjectId;
        SecretName = provider.ApiKeySecretName ?? "";
        FromEmail = ReadSetting(provider.SettingsJson, "fromEmail");
        FromName = ReadSetting(provider.SettingsJson, "fromName");
        MessageStream = ReadSetting(provider.SettingsJson, "messageStream")
            is { Length: > 0 } stream
            ? stream
            : "outbound";
        AccountSid = ReadSetting(provider.SettingsJson, "accountSid");
        FromNumber = ReadSetting(provider.SettingsJson, "fromNumber");
        Endpoint = ReadSetting(provider.SettingsJson, "endpoint");
        ShowForm = true;
        await LoadSecretsAsync();
    }

    public string BuildSettingsJson()
    {
        var settings = new JsonObject();
        if (Kind is "postmark" or "sendgrid")
        {
            settings["fromEmail"] = FromEmail.Trim();
            settings["fromName"] = FromName.Trim();
            if (Kind == "postmark")
                settings["messageStream"] = string.IsNullOrWhiteSpace(MessageStream)
                    ? DefaultMessageStream
                    : MessageStream.Trim();
        }
        else if (Kind == "twilio")
        {
            settings["accountSid"] = AccountSid.Trim();
            settings["fromNumber"] = FromNumber.Trim();
        }
        if (!string.IsNullOrWhiteSpace(Endpoint))
            settings["endpoint"] = Endpoint.Trim();
        return settings.ToJsonString();
    }

    public CommunicationProviderInput CurrentInput() =>
        new(
            Channel,
            Kind,
            Name,
            Enabled,
            AuthType,
            AuthType == "header" ? AuthHeaderName : null,
            RequiresSecret(AuthType) ? VaultProjectId : null,
            RequiresSecret(AuthType) ? SecretName : null,
            BuildSettingsJson()
        );

    public static bool RequiresSecret(string authType) =>
        authType is BearerAuth or HeaderAuth or BasicAuth;

    public async Task SaveAsync()
    {
        Saving = true;
        Message = null;
        try
        {
            if (EditingId is { } id)
                await InScopeAsync<CommunicationProviderService>(async service =>
                {
                    await service.UpdateAsync(id, CurrentInput());
                });
            else
                await InScopeAsync<CommunicationProviderService>(async service =>
                {
                    await service.CreateAsync(CurrentInput());
                });
            ShowForm = false;
            Report(EditingId is null ? "Provider added." : "Provider updated.");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Report(ex.Message, isError: true);
        }
        finally
        {
            Saving = false;
        }
    }

    public static CommunicationProviderInput ToInput(CommunicationProviderView provider) =>
        new(
            provider.Channel,
            provider.Kind,
            provider.Name,
            provider.Enabled,
            provider.AuthType,
            provider.AuthHeaderName,
            provider.VaultProjectId,
            provider.ApiKeySecretName,
            provider.SettingsJson
        );

    public async Task ToggleEnabledAsync(CommunicationProviderView provider)
    {
        try
        {
            var input = ToInput(provider) with { Enabled = !provider.Enabled };
            await InScopeAsync<CommunicationProviderService>(async service =>
            {
                await service.UpdateAsync(provider.Id, input);
            });
            Report($"Provider '{provider.Name}' {(input.Enabled ? "enabled" : "disabled")}.");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Report(ex.Message, isError: true);
        }
    }

    public async Task SetDefaultAsync(Guid id)
    {
        try
        {
            await InScopeAsync<CommunicationProviderService>(async service =>
            {
                await service.SetDefaultAsync(id);
            });
            Report("Default provider updated.");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Report(ex.Message, isError: true);
        }
    }

    public async Task ToggleTwoFactorAsync(CommunicationProviderView provider)
    {
        try
        {
            await InScopeAsync<CommunicationProviderService>(async service =>
            {
                await service.SetTwoFactorAsync(provider.Id, !provider.UseForTwoFactor);
            });
            Report(
                provider.UseForTwoFactor
                    ? $"Provider '{provider.Name}' is no longer used for two-factor codes."
                    : $"Provider '{provider.Name}' now delivers two-factor codes."
            );
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Report(ex.Message, isError: true);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            await InScopeAsync<CommunicationProviderService>(service => service.DeleteAsync(id));
            ConfirmDeleteId = null;
            Report("Provider deleted.");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Report(ex.Message, isError: true);
        }
    }

    public void StartTest(CommunicationProviderView provider)
    {
        ConfirmDeleteId = null;
        TestProviderId = TestProviderId == provider.Id ? null : provider.Id;
        TestRecipient = "";
    }

    public async Task SendTestAsync(Guid id)
    {
        TestSending = true;
        Message = null;
        try
        {
            var delivery = await InScopeAsync<DatabaseCommunicationSender, ClientMessageDelivery>(
                sender => sender.SendTestAsync(id, TestRecipient.Trim())
            );
            Report($"Test message sent via {delivery.Provider}.");
            TestProviderId = null;
        }
        catch (Exception ex)
        {
            Report(ex.Message, isError: true);
        }
        finally
        {
            TestSending = false;
        }
    }

    public static string ReadSetting(string json, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return
                document.RootElement.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = ScopeFactory.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        using var scope = ScopeFactory.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public static IReadOnlyList<CommunicationKindOption> KindsFor(CommunicationChannel channel) =>
        channel == CommunicationChannel.Sms
            ? [new(CommunicationProviderKind.Twilio, CommunicationChannel.Sms)]
            :
            [
                new(CommunicationProviderKind.Postmark, CommunicationChannel.Email),
                new(CommunicationProviderKind.SendGrid, CommunicationChannel.Email),
            ];

    public static string DefaultHeaderName(CommunicationProviderKind kind) =>
        kind == CommunicationProviderKind.Postmark ? "X-Postmark-Server-Token" : string.Empty;
}
