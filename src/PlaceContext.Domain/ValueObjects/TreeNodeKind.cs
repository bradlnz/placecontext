namespace PlaceContext.Domain.ValueObjects;

/// <summary>What a node in the decision tree represents.</summary>
public enum TreeNodeKind { Root, Decision, Change, File, Activity, Tool }
