using System.Reflection;

namespace ClientBehavioralTests;

public class AssemblyIdentityTests
{
    public static TheoryData<string> ContractAssemblies() =>
    [
        "spt-common", "spt-core", "spt-custom", "spt-debugging",
        "spt-prepatch", "spt-reflection", "spt-singleplayer",
    ];

    // The client contract's freeze marker: unlike the server DLLs (4.1.0.0 on
    // every 4.1.x patch), client DLLs are stamped per-patch.
    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void Contract_assembly_present_and_stamped_4_1_2(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        Assert.True(File.Exists(path), $"{name}.dll missing from test output — assembly closure copy broken");
        Assert.Equal(new Version(4, 1, 2, 0), AssemblyName.GetAssemblyName(path).Version);
    }
}
