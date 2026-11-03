using System.ComponentModel.DataAnnotations;

namespace PlaceContext.Host.Api;

public sealed record ProjectDataQueryRequest([Required, StringLength(32768)] string Sql);
