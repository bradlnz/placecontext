using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace PlaceContext.Host.Auth;

internal sealed record ServiceAppGrant(ServiceAppSession Session, string State, string ReturnPath);
