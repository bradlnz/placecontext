namespace PlaceContext.Domain.ValueObjects;

public sealed class VirtualMachineChainAction : ChainAction
{
    public const string ActionType = "qemu";

    public VirtualMachineChainAction(string imageProfile, string command, string outputField = "qemu_result",
        int memoryMb = 1024, int cpus = 1, int timeoutSeconds = 300, bool allowNetwork = false, bool pauseForLogin = false, Guid? loginProfileId = null)
    {
        imageProfile = imageProfile?.Trim() ?? "";
        if (imageProfile.Length is < 1 or > 80 || imageProfile.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
        {
            throw new ArgumentException("Choose an image profile using letters, numbers, hyphens or underscores (up to 80 characters).");
        }
        if (string.IsNullOrWhiteSpace(command) || command.Length > 8192 || command.Contains('\0'))
        {
            throw new ArgumentException("An agent task prompt is required (up to 8,192 characters).");
        }
        outputField = outputField?.Trim() ?? "";
        if (outputField.Length is < 1 or > 80 || !char.IsAsciiLetter(outputField[0])
            || outputField.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            throw new ArgumentException("Output field must start with a letter and contain letters, numbers or underscores (up to 80 characters).");
        }
        if (memoryMb is < 256 or > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryMb), "Memory must be 256–8192 MiB.");
        }
        if (cpus is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(cpus), "CPUs must be 1–8.");
        }
        if (timeoutSeconds is < 10 or > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be 10–3600 seconds.");
        }
        if (loginProfileId == Guid.Empty)
        {
            throw new ArgumentException("Choose a valid saved login.");
        }
        PauseForLogin = pauseForLogin;
        LoginProfileId = loginProfileId;
        ImageProfile = imageProfile;
        Command = command;
        OutputField = outputField;
        MemoryMb = memoryMb;
        Cpus = cpus;
        TimeoutSeconds = timeoutSeconds;
        AllowNetwork = allowNetwork;
    }

    public override string Type => ActionType;
    public override string DisplayName => "Browser workspace";
    public string ImageProfile
    {
        get;
    }
    // Retain the stored field name for existing workflow exports; its content is an agent prompt.
    public string Command
    {
        get;
    }
    public string OutputField
    {
        get;
    }
    public int MemoryMb
    {
        get;
    }
    public int Cpus
    {
        get;
    }
    public int TimeoutSeconds
    {
        get;
    }
    public bool AllowNetwork
    {
        get;
    }
    public bool PauseForLogin
    {
        get;
    }
    public Guid? LoginProfileId
    {
        get;
    }
}
