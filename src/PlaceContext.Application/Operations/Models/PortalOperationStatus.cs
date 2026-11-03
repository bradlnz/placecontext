using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.Operations;

public enum PortalOperationStatus
{
    Queued, Running, NeedsInput, Succeeded, Failed
}
