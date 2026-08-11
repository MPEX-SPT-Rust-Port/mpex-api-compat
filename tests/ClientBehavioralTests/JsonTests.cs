using SPT.Common.Http;
using SPT.Common.Utils;

namespace ClientBehavioralTests;

// Characterization: SPT.Common.Utils.Json is a thin Newtonsoft wrapper with
// DEFAULT settings — no camelCase contract resolver, no indentation. Mods and
// the server depend on this wire shape.
public class JsonTests
{
    [Fact]
    public void Serialize_uses_pascal_case_and_no_indentation()
    {
        var cfg = new ServerConfig("http://127.0.0.1:6969", "1.0", "2.0");
        Assert.Equal(
            """{"BackendUrl":"http://127.0.0.1:6969","MatchingVersion":"1.0","Version":"2.0"}""",
            Json.Serialize(cfg));
    }

    [Fact]
    public void Deserialize_roundtrips_a_dictionary()
    {
        var dict = Json.Deserialize<Dictionary<string, int>>("""{"a":1,"b":2}""");
        Assert.Equal(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, dict);
    }

    [Fact]
    public void SerializeIndented_produces_multiline_output()
    {
        var json = Json.SerializeIndented(new Dictionary<string, int> { ["a"] = 1 });
        Assert.Contains("\"a\": 1", json);
        Assert.NotEqual(Json.Serialize(new Dictionary<string, int> { ["a"] = 1 }), json);
    }
}
