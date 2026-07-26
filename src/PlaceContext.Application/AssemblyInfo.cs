using System.Runtime.CompilerServices;

// Pure helpers (e.g. AgentContextBuilder.ExtractMentionTerms) are internal so the test
// suite can exercise them directly without widening the public API surface.
[assembly: InternalsVisibleTo("PlaceContext.Application.Tests")]
