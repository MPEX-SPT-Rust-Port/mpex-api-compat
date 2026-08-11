using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using Xunit;

namespace BehavioralTests;

// Characterization tests: SPT 4.1.2 behavior is correct by definition.
// If one of these fails against the baseline, fix the TEST, not the library.
public class MongoIdTests
{
    [Fact]
    public void RoundTripsA24CharHexString()
    {
        var id = new MongoId("507f1f77bcf86cd799439011");
        Assert.Equal("507f1f77bcf86cd799439011", id.ToString());
    }

    [Fact]
    public void UppercaseInputIsNormalizedToLowercaseOutput()
    {
        var id = new MongoId("507F1F77BCF86CD799439011");
        Assert.Equal("507f1f77bcf86cd799439011", id.ToString());
    }

    [Fact]
    public void WrongLengthThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MongoId("abc123"));
    }

    [Fact]
    public void InvalidHexCharactersThrowFormatException()
    {
        Assert.Throws<FormatException>(() => new MongoId("zzzzzzzzzzzzzzzzzzzzzzzz"));
    }

    [Fact]
    public void EmptyAndNullStringsYieldEmptyId()
    {
        Assert.True(new MongoId("").IsEmpty);
        Assert.True(new MongoId((string?)null).IsEmpty);
        Assert.Equal(string.Empty, new MongoId("").ToString());
        Assert.Equal(new MongoId(""), MongoId.Empty());
    }

    [Fact]
    public void EqualityAndHashCodeAgreeForSameHex()
    {
        var a = new MongoId("507f1f77bcf86cd799439011");
        var b = new MongoId("507f1f77bcf86cd799439011");
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals("507f1f77bcf86cd799439011"));
    }

    [Fact]
    public void ImplicitConversionsRoundTrip()
    {
        MongoId fromString = "507f1f77bcf86cd799439011";
        string backToString = fromString;
        Assert.Equal("507f1f77bcf86cd799439011", backToString);
    }

    [Fact]
    public void GeneratedIdsAreValidAndDistinct()
    {
        var a = new MongoId();
        var b = new MongoId();
        Assert.NotEqual(a, b);
        Assert.True(a.IsValidMongoId());
        Assert.Equal(24, a.ToString().Length);
    }

    [Fact]
    public void CompareToIsByteOrderSensitiveNotLexicographic()
    {
        // 4.1.2 packs bytes with little-endian BitConverter, so CompareTo does NOT
        // match string ordering. A Rust port that compares lexicographically would
        // silently change sort behavior — this test pins the real 4.1.2 semantics.
        var one = new MongoId("000000000000000000000001");
        var two = new MongoId("000000000000000000000002");
        var ff = new MongoId("0000000000000000000000ff");

        Assert.Equal(0, one.CompareTo(new MongoId("000000000000000000000001")));
        Assert.True(one.CompareTo(two) < 0);
        // Lexicographically "..ff" > "..01", but the packed little-endian int is negative:
        Assert.True(ff.CompareTo(one) < 0);
    }

    [Fact]
    public void IsValidMongoIdExtensionChecksLengthAndHex()
    {
        Assert.True("507f1f77bcf86cd799439011".IsValidMongoId());
        Assert.False("507f1f77bcf86cd79943901".IsValidMongoId());   // 23 chars
        Assert.False("507f1f77bcf86cd79943901g".IsValidMongoId());  // non-hex char
        Assert.False("".IsValidMongoId());
    }

    [Fact]
    public void ToMongoIdsMapsStringsInOrder()
    {
        string[] source = ["507f1f77bcf86cd799439011", "507f191e810c19729de860ea"];
        var ids = source.ToMongoIds().ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal("507f1f77bcf86cd799439011", ids[0].ToString());
        Assert.Equal("507f191e810c19729de860ea", ids[1].ToString());
    }
}
