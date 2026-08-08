namespace PlaceContext.Domain.ValueObjects;

/// <summary>Runs a pre-built container image.</summary>
public sealed class ImageWorkload : WorkloadSource
{
    public ImageWorkload(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            throw new ArgumentException("Image must not be empty.", nameof(image));

        Image = image.Trim();
    }

    /// <summary>Container image reference such as <c>myorg/worker:latest</c>.</summary>
    public string Image { get; }

    public override string Label => Image;
}
