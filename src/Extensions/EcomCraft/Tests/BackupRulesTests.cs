using System.IO;
using System.IO.Compression;
using System.Text;
using EcomCraftStudio.Studio;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Backup manifest rules (F45): a restore only accepts zips whose every file matches the recorded size and SHA-256.</summary>
public class BackupRulesTests
{
    /// <summary>A backup zip in read mode, built in memory from the given files.</summary>
    private static (MemoryStream Stream, ZipArchive Zip) ZipOf(params (string Name, string Content)[] files)
    {
        MemoryStream stream = new();
        using (ZipArchive write = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in files)
            {
                ZipArchiveEntry entry = write.CreateEntry(name);
                using StreamWriter writer = new(entry.Open());
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return (stream, new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true));
    }

    /// <summary>A manifest describing the given in-memory contents.</summary>
    private static JObject ManifestOf(params (string Name, string Content)[] files)
    {
        JArray list = [];
        foreach ((string name, string content) in files)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            list.Add(BackupRules.Entry(name, bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()));
        }
        return new JObject() { ["version"] = 1, ["files"] = list };
    }

    [Fact]
    public void MatchingBackupPasses()
    {
        (MemoryStream stream, ZipArchive zip) = ZipOf(("ecomcraft/storage.json", "{}"), ("manifest.json", "{}"));
        try
        {
            Assert.Empty(BackupRules.Validate(zip, ManifestOf(("ecomcraft/storage.json", "{}"))));
        }
        finally
        {
            zip.Dispose();
            stream.Dispose();
        }
    }

    [Fact]
    public void ChangedContentMissingFileAndExtraFileAllFail()
    {
        (MemoryStream stream, ZipArchive zip) = ZipOf(("ecomcraft/a.json", "one"), ("ecomcraft/extra.json", "x"));
        try
        {
            JObject manifest = ManifestOf(("ecomcraft/a.json", "one"), ("ecomcraft/b.json", "two"));
            List<string> problems = BackupRules.Validate(zip, manifest);
            Assert.Contains(problems, problem => problem.Contains("ecomcraft/b.json"));
            Assert.Contains(problems, problem => problem.Contains("extra.json"));
        }
        finally
        {
            zip.Dispose();
            stream.Dispose();
        }
        (MemoryStream other, ZipArchive tampered) = ZipOf(("ecomcraft/a.json", "changed"));
        try
        {
            List<string> problems = BackupRules.Validate(tampered, ManifestOf(("ecomcraft/a.json", "one")));
            Assert.Contains(problems, problem => problem.Contains("ecomcraft/a.json"));
        }
        finally
        {
            tampered.Dispose();
            other.Dispose();
        }
    }

    [Fact]
    public void DamagedManifestAndUnsafePathsAreRejected()
    {
        (MemoryStream stream, ZipArchive zip) = ZipOf(("ecomcraft/a.json", "one"));
        try
        {
            Assert.NotEmpty(BackupRules.Validate(zip, new JObject() { ["files"] = new JArray() }));
            JArray unsafeList = new() { BackupRules.Entry("../outside.json", 1, "00") };
            Assert.NotEmpty(BackupRules.Validate(zip, new JObject() { ["files"] = unsafeList }));
        }
        finally
        {
            zip.Dispose();
            stream.Dispose();
        }
    }

    [Theory]
    [InlineData("ecomcraft/C:/x")]
    [InlineData("ecomcraft/C:\\x")]
    [InlineData("ecomcraft//x")]
    [InlineData("ecomcraft/\\x")]
    [InlineData("ecomcraft/\\\\server\\share\\x")]
    [InlineData("ecomcraft/a/../../x")]
    [InlineData("ecomcraft/..")]
    [InlineData("ecomcraft/.")]
    [InlineData("ecomcraft/")]
    [InlineData("ecomcraft/a:stream")]
    [InlineData("other/x")]
    [InlineData("/ecomcraft/x")]
    [InlineData("")]
    public void EscapingEntryNamesHaveNoDestination(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "ecomcraft-tests", "restore");
        Assert.Null(BackupRules.ResolveDestination(name, root));
    }

    [Fact]
    public void EscapingNamesInTheManifestFailValidation()
    {
        foreach (string name in new[] { "ecomcraft/C:/x", "ecomcraft//x", "ecomcraft/\\x", "ecomcraft/a/../../x" })
        {
            (MemoryStream stream, ZipArchive zip) = ZipOf((name, "one"));
            try
            {
                Assert.Contains("清单包含不安全的路径。", BackupRules.Validate(zip, ManifestOf((name, "one"))));
            }
            finally
            {
                zip.Dispose();
                stream.Dispose();
            }
        }
    }

    [Fact]
    public void NormalEntryNamesStayInsideTheRoot()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ecomcraft-tests", "restore"));
        Assert.Equal(Path.Combine(root, "storage.json"), BackupRules.ResolveDestination("ecomcraft/storage.json", root));
        Assert.Equal(Path.Combine(root, "product-files", "local", "a", "original.jpg"), BackupRules.ResolveDestination("ecomcraft/product-files/local/a/original.jpg", root));
        Assert.Equal(Path.Combine(root, "storage.json"), BackupRules.ResolveDestination("ecomcraft/storage.json", root + Path.DirectorySeparatorChar));
        Assert.True(BackupRules.IsSafeEntryName("accounts/Users.ldb"));
    }

    [Fact]
    public void HashMatchesFileContent()
    {
        string file = Path.Combine(Path.GetTempPath(), "ecomcraft-tests", "backup-hash.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, "stable");
        Assert.Equal(BackupRules.Hash(file), BackupRules.Hash(file));
        Assert.Equal(64, BackupRules.Hash(file).Length);
    }

    [Fact]
    public void EntryNamesSeparateEcomCraftDataFromAccountData()
    {
        string root = Path.Combine(Path.GetTempPath(), "ecomcraft-tests", "naming");
        string data = Path.Combine(root, "Data");
        string ecomcraft = Path.Combine(data, "EcomCraft");
        Assert.Equal("ecomcraft/storage.json", BackupRules.EntryName(Path.Combine(ecomcraft, "storage.json"), ecomcraft, data));
        Assert.Equal("ecomcraft/product-files/local/a/original.jpg", BackupRules.EntryName(Path.Combine(ecomcraft, "product-files", "local", "a", "original.jpg"), ecomcraft, data));
        Assert.Equal("accounts/Users.ldb", BackupRules.EntryName(Path.Combine(data, "Users.ldb"), ecomcraft, data));
        Assert.Equal("accounts/Roles.fds", BackupRules.EntryName(Path.Combine(data, "Roles.fds"), ecomcraft, data));
    }
}
