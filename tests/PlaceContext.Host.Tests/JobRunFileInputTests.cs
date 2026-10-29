using System.Text.Json;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobRunFileInputTests
{
    [Fact]
    public void Keeps_nested_objects_numbers_arrays_and_file_references()
    {
        const string json = "{\"orderId\":123,\"customer\":{\"name\":\"Ada\"},\"items\":[1,2],\"attachment\":{\"$file\":{\"key\":\"upload\"}}}";
        Assert.Equal(json, JobsViewModel.ValidateRunFile(json, [new("orderId", Required: true)]));
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\" \"}")]
    public void Rejects_nonobjects_and_missing_required_values(string json) =>
        Assert.Throws<ArgumentException>(() => JobsViewModel.ValidateRunFile(json, [new("name", Required: true)]));
    [Fact]
    public void Rejects_invalid_and_oversized_json()
    {
        Assert.ThrowsAny<JsonException>(() => JobsViewModel.ValidateRunFile("{invalid", []));
        Assert.Throws<ArgumentException>(() => JobsViewModel.ValidateRunFile(new string('x', 1_048_577), []));
    }
}
