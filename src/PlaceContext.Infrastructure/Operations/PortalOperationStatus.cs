using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Operations;

public enum PortalOperationStatus { Queued, Running, Succeeded, Failed }
