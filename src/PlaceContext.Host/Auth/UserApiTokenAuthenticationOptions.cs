using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Auth;

public sealed class UserApiTokenAuthenticationOptions : AuthenticationSchemeOptions { }
