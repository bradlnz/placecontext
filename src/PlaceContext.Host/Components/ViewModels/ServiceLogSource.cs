using System.Globalization;
using System.Text;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed record ServiceLogSource(Guid Id, Guid ProjectId, string Name, string ProjectName, string Status);
