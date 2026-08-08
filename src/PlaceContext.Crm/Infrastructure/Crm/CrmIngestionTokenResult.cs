using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Crm.Infrastructure.Crm;

public sealed record CrmIngestionTokenResult(CrmIngestionSettingsView Settings, string Token);
