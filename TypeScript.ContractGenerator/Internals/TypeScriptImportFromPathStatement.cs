using SkbKontur.TypeScript.ContractGenerator.CodeDom;

namespace SkbKontur.TypeScript.ContractGenerator.Internals
{
    internal class TypeScriptImportFromPathStatement : TypeScriptImportStatement
    {
        public override string GenerateCode(ICodeGenerationContext context)
        {
            return $"import type {{ {TypeName} }} from '{context.GetReferenceFromUnitToAnother(CurrentUnit.Path, PathToUnit)}';";
        }

        public string TypeName { get; set; }
        public TypeScriptUnit CurrentUnit { get; set; }
        public string PathToUnit { get; set; }
    }
}
