namespace PlaceContext.Domain.ValueObjects;

public sealed class ContainerRunChainAction : ChainAction
{
    public const string ActionType = "container_run";
    public ContainerRunChainAction(string image = "", string imageField = "container_build.image", string outputField = "container_run",
        IReadOnlyList<string>? command = null, int? port = null, int memoryMb = 512, int cpus = 1, int timeoutSeconds = 300, bool allowNetwork = false)
    {
        image = image?.Trim() ?? "";
        imageField = imageField?.Trim() ?? "";
        if ((image.Length == 0) == (imageField.Length == 0))
        {
            throw new ArgumentException("Choose an image reference or a workflow image field.");
        }

        if (image.Length > 0)
        {
            ValidateImage(image);
        }

        if (imageField.Length > 0)
        {
            ValidateField(imageField);
        }

        ValidateField(outputField, nested: false);
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentException("Use a web port from 1024 to 65535.");
        }

        if (memoryMb is < 64 or > 8192 || cpus is < 1 or > 8)
        {
            throw new ArgumentException("Use 64–8192 MiB and 1–8 CPUs.");
        }

        if (timeoutSeconds is < 10 or > 3600)
        {
            throw new ArgumentException("Timeout must be 10–3600 seconds.");
        }

        if (command is { Count: > 64 } || command?.Any(a => a is null || a.Length > 8192 || a.Contains('\0')) == true)
        {
            throw new ArgumentException("Use up to 64 command arguments of at most 8,192 characters each.");
        }

        Image = image;
        ImageField = imageField;
        OutputField = outputField;
        Command = command?.ToArray() ?? [];
        Port = port;
        MemoryMb = memoryMb;
        Cpus = cpus;
        TimeoutSeconds = timeoutSeconds;
        AllowNetwork = allowNetwork;
    }
    public override string Type => ActionType;
    public override string DisplayName => "Run container";
    public string Image
    {
        get;
    }
    public string ImageField
    {
        get;
    }
    public string OutputField
    {
        get;
    }
    public IReadOnlyList<string> Command
    {
        get;
    }
    public int? Port
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
    public static void ValidateImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image) || image.Length > 256 || !char.IsAsciiLetterOrDigit(image[0])
            || image.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '/' or ':' or '_' or '-' or '@')))
        {
            throw new ArgumentException("Enter a valid container image reference.");
        }
    }
    internal static void ValidateField(string field, bool nested = true)
    {
        if (string.IsNullOrEmpty(field) || field.Length > 80 || field.Split('.').Any(part => part.Length == 0
            || !char.IsAsciiLetter(part[0]) || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) || !nested && field.Contains('.'))
        {
            throw new ArgumentException("Use a workflow field starting with a letter, with letters, numbers and underscores.");
        }
    }
}
