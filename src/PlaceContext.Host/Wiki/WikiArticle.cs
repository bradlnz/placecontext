using System.Collections.Immutable;
using System.Reflection;
using Markdig;

namespace PlaceContext.Host.Wiki;

/// <summary>One wiki article: its url slug, display title, one-line summary, and rendered HTML.</summary>
public sealed record WikiArticle(string Slug, string Title, string Summary, string Html);
