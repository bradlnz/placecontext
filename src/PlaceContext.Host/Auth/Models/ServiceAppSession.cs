using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace PlaceContext.Host.Auth;

public sealed record ServiceAppSession(Guid TenantId, Guid UserId, string Role, Guid ProjectId, Guid ServiceId, DateTimeOffset ExpiresAt);
