namespace PlaceContext.Host.Components.ViewModels;

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public enum ChatResultKind
{
    Text,
    Graph,
    Map,
    Artifact,
}

public enum ChatSettingsTab
{
    Prompt,
    Rag,
    Commands,
}

public static class ChatPresentationCatalog
{
    public static ChatRole ParseRole(string value) =>
        value switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            _ => ChatRole.System,
        };

    public static ChatResultKind ParseResultKind(string value) =>
        value switch
        {
            "graph" => ChatResultKind.Graph,
            "map" => ChatResultKind.Map,
            "artifact" => ChatResultKind.Artifact,
            _ => ChatResultKind.Text,
        };

    public static ChatSettingsTab ParseSettingsTab(string value) =>
        value switch
        {
            "rag" => ChatSettingsTab.Rag,
            "commands" => ChatSettingsTab.Commands,
            _ => ChatSettingsTab.Prompt,
        };

    public static string SettingsKey(ChatSettingsTab value) =>
        value switch
        {
            ChatSettingsTab.Rag => "rag",
            ChatSettingsTab.Commands => "commands",
            _ => "prompt",
        };
}
