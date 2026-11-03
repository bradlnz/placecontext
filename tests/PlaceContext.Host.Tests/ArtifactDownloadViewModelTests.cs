using System.Text;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ArtifactDownloadViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Downloads_preserve_all_bytes_and_release_the_stream(bool binary)
    {
        var bytes = binary ? Enumerable.Range(0, 256).Select(i => (byte)i).ToArray() : Encoding.UTF8.GetBytes(new string('x', 1_000_000) + "😀tail");
        var artifact = new RunArtifactView("output.log", binary ? Convert.ToBase64String(bytes) : Encoding.UTF8.GetString(bytes), binary);
        var js = new Js();
        var vm = new ArtifactDownloadViewModel(js);
        await vm.DownloadAsync(() => artifact);
        Assert.Equal(bytes, js.Bytes);
        Assert.Equal("output.log", js.Filename);
        Assert.False(js.Stream!.CanRead);
        Assert.False(vm.Busy);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Failed_downloads_allow_retry()
    {
        var js = new Js { Fail = true };
        var vm = new ArtifactDownloadViewModel(js);
        await vm.DownloadAsync(() => new("output.log", "full log"));
        Assert.NotNull(vm.Error);
        Assert.False(vm.Busy);
        Assert.False(js.Stream!.CanRead);
        js.Fail = false;
        await vm.DownloadAsync(() => new("output.log", "full log"));
        Assert.Null(vm.Error);
        Assert.Equal("full log", Encoding.UTF8.GetString(js.Bytes!));
    }

    private sealed class Js : IJSRuntime
    {
        public byte[]? Bytes;
        public string? Filename;
        public Stream? Stream;
        public bool Fail;
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args)
        {
            Assert.Equal("pcdata.downloadStream", name);
            Filename = Assert.IsType<string>(args![0]);
            Stream = Assert.IsType<DotNetStreamReference>(args[1]).Stream;
            if (Fail)
            {
                throw new JSException("Failed");
            }
            using var copy = new MemoryStream();
            await Stream.CopyToAsync(copy, ct);
            Bytes = copy.ToArray();
            return default!;
        }
    }
}
