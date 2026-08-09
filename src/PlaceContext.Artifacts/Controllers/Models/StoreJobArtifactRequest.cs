using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Artifacts.Contracts.Api;

namespace PlaceContext.Artifacts.Controllers;

public sealed record StoreJobArtifactRequest(
    Guid ProjectId,
    Guid JobId,
    Guid RunId,
    string JobName,
    string Kind,
    string FileName,
    string Title,
    string ContentType,
    string ContentBase64);
