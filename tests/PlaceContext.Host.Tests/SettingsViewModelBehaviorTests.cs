using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class SettingsViewModelBehaviorTests
{
    [Fact]
    public void Communication_catalogs_are_typed_and_channel_specific()
    {
        Assert.Equal(
            [CommunicationChannel.Email, CommunicationChannel.Email],
            CommunicationsSettingsViewModel
                .KindsFor(CommunicationChannel.Email)
                .Select(kind => kind.Channel)
        );
        Assert.Equal(
            [CommunicationProviderKind.Twilio],
            CommunicationsSettingsViewModel
                .KindsFor(CommunicationChannel.Sms)
                .Select(kind => kind.Kind)
        );
        Assert.Equal(
            "X-Postmark-Server-Token",
            CommunicationsSettingsViewModel.DefaultHeaderName(CommunicationProviderKind.Postmark)
        );
    }

    [Fact]
    public void Access_view_model_excludes_owner_and_current_user_from_removal()
    {
        var currentUserId = Guid.NewGuid();
        var owner = new MemberView(
            Guid.NewGuid(),
            "owner@example.com",
            "Owner",
            "Owner",
            false,
            DateTimeOffset.UtcNow
        );
        var currentUser = new MemberView(
            currentUserId,
            "me@example.com",
            "Me",
            "Member",
            false,
            DateTimeOffset.UtcNow
        );
        var member = new MemberView(
            Guid.NewGuid(),
            "member@example.com",
            "Member",
            "Member",
            false,
            DateTimeOffset.UtcNow
        );

        Assert.False(AccessSettingsViewModel.CanRemove(owner, currentUserId));
        Assert.False(AccessSettingsViewModel.CanRemove(currentUser, currentUserId));
        Assert.True(AccessSettingsViewModel.CanRemove(member, currentUserId));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("invalid", false)]
    [InlineData("valid@example.com", true)]
    public void Access_email_validation_matches_existing_invite_behavior(
        string email,
        bool expected
    )
    {
        Assert.Equal(expected, AccessSettingsViewModel.IsValidEmail(email));
    }
}
