using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Application.Cluster;

public sealed class LaunchClusterAgentHandler : ICommandHandler<LaunchClusterAgentCommand, LaunchAgentResult>
{
    /// <summary>Vault secret names (see <see cref="SystemProjects.Cluster"/>) — a Tailscale OAuth
    /// client with the "Devices Core: Write" scope, plus an optional default ACL tag for minted keys.</summary>
    public const string ClientIdSecretName = "TS_CLIENT_ID";
    public const string ClientSecretSecretName = "TS_CLIENT_SECRET";
    public const string TagSecretName = "TS_TAG";
    public const string DefaultTag = "tag:agent";

    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;
    private readonly ITailscaleKeyMinter _minter;
    private readonly IClusterAdminPort _admin;

    public LaunchClusterAgentHandler(
        IProjectSecretRepository secrets, ISecretProtector protector, ITailscaleKeyMinter minter, IClusterAdminPort admin)
        => (_secrets, _protector, _minter, _admin) = (secrets, protector, minter, admin);

    public async Task<LaunchAgentResult> HandleAsync(LaunchClusterAgentCommand command, CancellationToken ct = default)
    {
        var ciphers = await _secrets.GetCiphersAsync(SystemProjects.Cluster, ct);
        if (!ciphers.TryGetValue(ClientIdSecretName, out var clientIdCipher)
            || !ciphers.TryGetValue(ClientSecretSecretName, out var clientSecretCipher))
        {
            return new LaunchAgentResult(
                Minted: false,
                JoinCode: null,
                ServerUrl: null,
                ConnectCommand: "",
                Message: $"Add '{ClientIdSecretName}' and '{ClientSecretSecretName}' to the vault "
                    + "(cluster system project) — a Tailscale OAuth client with device-write access — "
                    + "to mint agent join keys.");
        }

        var clientId = _protector.Unprotect(clientIdCipher);
        var clientSecret = _protector.Unprotect(clientSecretCipher);
        var tags = ciphers.TryGetValue(TagSecretName, out var tagCipher) ? _protector.Unprotect(tagCipher) : DefaultTag;
        if (string.IsNullOrWhiteSpace(tags)) tags = DefaultTag;

        var tsKey = await _minter.MintEphemeralAgentKeyAsync(clientId, clientSecret, tags, ct);
        if (string.IsNullOrWhiteSpace(tsKey))
        {
            return new LaunchAgentResult(
                Minted: false,
                JoinCode: null,
                ServerUrl: null,
                ConnectCommand: "",
                Message: "Tailscale did not return an auth key — check TS_CLIENT_ID/TS_CLIENT_SECRET "
                    + "are a valid OAuth client with device-write access.");
        }

        var join = await _admin.GetJoinMaterialAsync(tsKey, ct);
        if (join is null)
        {
            return new LaunchAgentResult(
                Minted: true,
                JoinCode: null,
                ServerUrl: null,
                ConnectCommand: "",
                Message: "Minted a Tailscale auth key, but the cluster join secret is not seeded yet "
                    + "— deploy/upgrade the k3s master first, then try again.");
        }

        return new LaunchAgentResult(
            Minted: true,
            JoinCode: join.JoinCode,
            ServerUrl: join.ServerUrl,
            ConnectCommand: $"sudo placecontext connect --code {join.JoinCode}",
            Message: "Minted a fresh Tailscale auth key and built a join code — valid for one new agent.");
    }
}
