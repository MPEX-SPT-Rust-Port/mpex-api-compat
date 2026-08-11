using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SPT.PrePatch;

namespace ClientBehavioralTests;

// Characterization of the Cecil-based prepatch logic — the one client slice
// with real behavior that runs without Unity. EnumPatcher resolves
// EFT.JsonEnumNameAttribute from the assembly being patched, so the fixture
// defines it alongside the enum.
public class EnumPatcherTests
{
    private static readonly ManualLogSource Log = new("EnumPatcherTests");

    private static AssemblyDefinition BuildFixtureAssembly()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Fixture", new Version(1, 0)), "Fixture", ModuleKind.Dll);
        var module = asm.MainModule;

        // public enum EFT.TestEnum { Existing = 0 }
        var enumType = new TypeDefinition("EFT", "TestEnum",
            TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
        enumType.Fields.Add(new FieldDefinition("value__",
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            module.TypeSystem.Int32));
        enumType.Fields.Add(new FieldDefinition("Existing",
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
            enumType) { Constant = 0 });
        module.Types.Add(enumType);

        // public class EFT.JsonEnumNameAttribute : Attribute { public JsonEnumNameAttribute(string) {} }
        var attr = new TypeDefinition("EFT", "JsonEnumNameAttribute",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit, module.ImportReference(typeof(Attribute)));
        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        ctor.Body.GetILProcessor().Emit(OpCodes.Ret);
        attr.Methods.Add(ctor);
        module.Types.Add(attr);

        return asm;
    }

    [Fact]
    public void Empty_entry_list_is_a_noop()
    {
        var asm = BuildFixtureAssembly();
        EnumPatcher.PatchEnums(Log, ref asm, Array.Empty<EnumEntryDefinition>());
        Assert.Single(asm.MainModule.GetType("EFT.TestEnum").Fields, f => f.Name != "value__");
    }

    [Fact]
    public void Adds_constant_with_json_name_attribute()
    {
        var asm = BuildFixtureAssembly();
        var entries = new[]
        {
            new EnumEntryDefinition
            {
                EnumType = "EFT.TestEnum", ConstantName = "Added", JsonEnumName = "added", ConstantValue = 7,
            },
        };

        EnumPatcher.PatchEnums(Log, ref asm, entries);

        var field = asm.MainModule.GetType("EFT.TestEnum").Fields.Single(f => f.Name == "Added");
        Assert.Equal(7, Convert.ToInt64(field.Constant));
        var attr = Assert.Single(field.CustomAttributes);
        Assert.Equal("EFT.JsonEnumNameAttribute", attr.AttributeType.FullName);
        Assert.Equal("added", attr.ConstructorArguments.Single().Value);
    }

    [Fact]
    public void Unknown_enum_type_throws()
    {
        var asm = BuildFixtureAssembly();
        var entries = new[]
        {
            new EnumEntryDefinition { EnumType = "EFT.Missing", ConstantName = "X", ConstantValue = 1 },
        };
        var ex = Assert.Throws<InvalidOperationException>(() => EnumPatcher.PatchEnums(Log, ref asm, entries));
        Assert.Contains("EFT.Missing", ex.Message);
    }

    [Fact]
    public void Blank_constant_name_throws()
    {
        var asm = BuildFixtureAssembly();
        var entries = new[]
        {
            new EnumEntryDefinition { EnumType = "EFT.TestEnum", ConstantName = " ", ConstantValue = 1 },
        };
        Assert.Throws<InvalidOperationException>(() => EnumPatcher.PatchEnums(Log, ref asm, entries));
    }

    [Fact]
    public void Duplicate_constant_name_throws()
    {
        var asm = BuildFixtureAssembly();
        var entries = new[]
        {
            new EnumEntryDefinition { EnumType = "EFT.TestEnum", ConstantName = "Existing", ConstantValue = 9 },
        };
        Assert.Throws<InvalidOperationException>(() => EnumPatcher.PatchEnums(Log, ref asm, entries));
    }

    [Fact]
    public void Duplicate_constant_value_throws()
    {
        var asm = BuildFixtureAssembly();
        var entries = new[]
        {
            new EnumEntryDefinition { EnumType = "EFT.TestEnum", ConstantName = "Zero2", ConstantValue = 0 },
        };
        Assert.Throws<InvalidOperationException>(() => EnumPatcher.PatchEnums(Log, ref asm, entries));
    }
}
