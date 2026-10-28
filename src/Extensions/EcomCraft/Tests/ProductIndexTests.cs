using EcomCraftStudio.Studio.Products;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Product library (F36): accounts only see, rename and delete their own products.</summary>
public class ProductIndexTests
{
    /// <summary>An index with two products of alice and one of bob.</summary>
    private static ProductIndex Index()
    {
        ProductIndex index = new();
        index.Add(new ProductRecord() { Id = "a1", UserId = "alice", Name = "Mug", Created = 1 });
        index.Add(new ProductRecord() { Id = "a2", UserId = "alice", Name = "Bottle", Created = 2 });
        index.Add(new ProductRecord() { Id = "b1", UserId = "bob", Name = "Shoe", Created = 3 });
        return index;
    }

    [Fact]
    public void ListsOnlyTheAccountsProductsNewestFirst()
    {
        Assert.Equal(new List<string> { "a2", "a1" }, Index().ForUser("alice").Select(item => item.Id).ToList());
        Assert.Equal(new List<string> { "b1" }, Index().ForUser("bob").Select(item => item.Id).ToList());
        Assert.Empty(Index().ForUser("carol"));
    }

    [Fact]
    public void AnotherAccountsProductIsNeverFoundRenamedOrRemoved()
    {
        ProductIndex index = Index();
        Assert.Null(index.Find("alice", "b1"));
        Assert.NotNull(index.Rename("alice", "b1", "Mine now"));
        Assert.Equal("Shoe", index.Find("bob", "b1").Name);
        Assert.Null(index.Remove("alice", "b1"));
        Assert.Equal(3, index.Items.Count);
    }

    [Fact]
    public void OwnerCanRenameAndRemove()
    {
        ProductIndex index = Index();
        Assert.Null(index.Rename("alice", "a1", "  Coffee mug  "));
        Assert.Equal("Coffee mug", index.Find("alice", "a1").Name);
        Assert.Equal("a1", index.Remove("alice", "a1").Id);
        Assert.Null(index.Find("alice", "a1"));
        Assert.Equal(2, index.Items.Count);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ok", "ok")]
    [InlineData("tab\there", null)]
    public void NamesAreTrimmedAndChecked(string name, string expected)
    {
        Assert.Equal(expected, ProductIndex.CleanName(name));
    }

    [Fact]
    public void EachAccountHasALimit()
    {
        ProductIndex index = new();
        for (int i = 0; i < ProductIndex.MaxPerUser; i++)
        {
            Assert.Null(index.Add(new ProductRecord() { Id = $"p{i}", UserId = "alice" }));
        }
        Assert.NotNull(index.Add(new ProductRecord() { Id = "over", UserId = "alice" }));
        Assert.Null(index.Add(new ProductRecord() { Id = "other", UserId = "bob" }));
    }
}
