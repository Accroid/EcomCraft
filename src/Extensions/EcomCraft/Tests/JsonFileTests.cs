using System.IO;
using EcomCraftStudio.Studio.Common;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Data files (R04): a file that exists but cannot be read is reported as such, and a failed write leaves the original unchanged.</summary>
public class JsonFileTests : IDisposable
{
    /// <summary>Folder for this test's files.</summary>
    private readonly string Folder = Path.Combine(Path.GetTempPath(), $"ecomcraft-jsonfile-{Guid.NewGuid():N}");

    /// <summary>The data file under test.</summary>
    private string File1 => Path.Combine(Folder, "data.json");

    public JsonFileTests()
    {
        Directory.CreateDirectory(Folder);
    }

    public void Dispose()
    {
        Directory.Delete(Folder, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MissingFileIsMissing()
    {
        Assert.Equal(JsonFileState.Missing, JsonFile.TryReadObject(File1, out JObject value, out string error));
        Assert.Null(value);
        Assert.Null(error);
    }

    [Fact]
    public void ValidFileReads()
    {
        File.WriteAllText(File1, "{ \"version\": 1 }");
        Assert.Equal(JsonFileState.Ok, JsonFile.TryReadObject(File1, out JObject value, out string error));
        Assert.Equal(1, (int)value["version"]);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1, 2]")]
    public void DamagedFileIsUnreadable(string content)
    {
        File.WriteAllText(File1, content);
        Assert.Equal(JsonFileState.Unreadable, JsonFile.TryReadObject(File1, out JObject value, out string error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ConversionOfBadDataIsUnreadable()
    {
        File.WriteAllText(File1, "{ \"items\": \"not a list\" }");
        Assert.Equal(JsonFileState.Unreadable, JsonFile.TryRead<List<int>>(File1, root => root["items"].ToObject<List<int>>(), out List<int> value, out string error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ConversionOfMissingFileIsMissing()
    {
        Assert.Equal(JsonFileState.Missing, JsonFile.TryRead<List<int>>(File1, root => root["items"].ToObject<List<int>>(), out List<int> value, out _));
        Assert.Null(value);
    }

    [Fact]
    public void WriteCreatesFoldersAndLeavesNoTemporaryFile()
    {
        string nested = Path.Combine(Folder, "a", "b", "data.json");
        Assert.True(JsonFile.TryWriteAtomic(nested, "{ \"version\": 2 }", out string error));
        Assert.Null(error);
        Assert.Equal("{ \"version\": 2 }", File.ReadAllText(nested));
        Assert.False(File.Exists($"{nested}.tmp"));
    }

    [Fact]
    public void WriteReplacesTheOriginal()
    {
        File.WriteAllText(File1, "old");
        Assert.True(JsonFile.TryWriteAtomic(File1, "new", out _));
        Assert.Equal("new", File.ReadAllText(File1));
    }

    [Fact]
    public void FailedWriteLeavesTheOriginalUnchanged()
    {
        File.WriteAllText(File1, "original");
        // A folder where the temporary file should go makes the write fail on every system.
        Directory.CreateDirectory($"{File1}.tmp");
        Assert.False(JsonFile.TryWriteAtomic(File1, "new", out string error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal("original", File.ReadAllText(File1));
    }
}
