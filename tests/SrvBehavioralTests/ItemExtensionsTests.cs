using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace BehavioralTests;

// Characterization tests: SPT 4.1.2 behavior is correct by definition.
public class ItemExtensionsTests
{
    private static readonly MongoId RootId = new("600000000000000000000001");
    private static readonly MongoId ChildId = new("600000000000000000000002");
    private static readonly MongoId GrandchildId = new("600000000000000000000003");
    private static readonly MongoId UnrelatedId = new("600000000000000000000004");

    private static List<Item> BuildTree()
    {
        return
        [
            new Item { Id = RootId, Template = new MongoId("5aafa857e5b5b00018480968") },
            new Item { Id = ChildId, Template = new MongoId("5aafa857e5b5b00018480968"), ParentId = RootId, SlotId = "mod_stock" },
            new Item { Id = GrandchildId, Template = new MongoId("5aafa857e5b5b00018480968"), ParentId = ChildId, SlotId = "mod_pistol_grip" },
            new Item { Id = UnrelatedId, Template = new MongoId("5aafa857e5b5b00018480968") },
        ];
    }

    [Fact]
    public void GetItemStackSizeDefaultsToOneWithoutUpd()
    {
        var item = new Item { Id = RootId, Template = new MongoId("5aafa857e5b5b00018480968") };
        Assert.Equal(1, item.GetItemStackSize());
    }

    [Fact]
    public void GetItemStackSizeReadsUpdStackObjectsCount()
    {
        var item = new Item
        {
            Id = RootId,
            Template = new MongoId("5aafa857e5b5b00018480968"),
            Upd = new Upd { StackObjectsCount = 5 },
        };
        Assert.Equal(5, item.GetItemStackSize());
    }

    [Fact]
    public void GenerateItemsMapKeysItemsById()
    {
        var map = BuildTree().GenerateItemsMap();
        Assert.Equal(4, map.Count);
        Assert.Same(map[ChildId], map.Values.Single(i => i.Id == ChildId));
    }

    [Fact]
    public void GetItemWithChildrenWalksTheFullSubtreeAndSkipsUnrelatedItems()
    {
        var result = BuildTree().GetItemWithChildren(RootId);
        Assert.Equal(3, result.Count);
        Assert.Equal(RootId, result[0].Id); // root always first
        Assert.Contains(result, i => i.Id == ChildId);
        Assert.Contains(result, i => i.Id == GrandchildId);
        Assert.DoesNotContain(result, i => i.Id == UnrelatedId);
    }

    [Fact]
    public void GetItemWithChildrenReturnsEmptyForMissingRoot()
    {
        var result = BuildTree().GetItemWithChildren(new MongoId("6000000000000000000000ff"));
        Assert.Empty(result);
    }
}
