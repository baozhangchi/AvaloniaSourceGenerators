using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaSourceGenerators;

[Generator(LanguageNames.CSharp)]
internal class RaiseAndSetIfChangedGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var typesProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
            static (generatorExecutionContext, _) =>
            {
                var classDeclarationSyntax = (ClassDeclarationSyntax)generatorExecutionContext.Node;
                var declaredSymbol =
                    generatorExecutionContext.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax)!;
                var propertySymbols = classDeclarationSyntax.ChildNodes().OfType<PropertyDeclarationSyntax>()
                    .Select(x => generatorExecutionContext.SemanticModel.GetDeclaredSymbol(x)!)
                    .Where(x => x.GetAttributes().Any(a =>
                        a.AttributeClass!.ToDisplayString() == typeof(RaiseAndSetIfChangedAttribute).FullName))
                    .ToList();
                return
                (
                    ClassDeclarationSyntax: classDeclarationSyntax,
                    NamedTypeSymbol: declaredSymbol,
                    PropertySymbols: propertySymbols
                );
            });
        context.RegisterSourceOutput(typesProvider.Combine(context.CompilationProvider), GenerateCode);
    }

    private void GenerateCode(SourceProductionContext arg1, ((ClassDeclarationSyntax ClassDeclarationSyntax, INamedTypeSymbol NamedTypeSymbol, List<IPropertySymbol> PropertySymbols) Left, Compilation Right) arg2)
    {
        if (arg2.Right is CSharpCompilation compilation)
        {
            var nullable = compilation.Options.NullableContextOptions == NullableContextOptions.Enable;
            if (compilation.IsReferenced("ReactiveUI.Avalonia"))
            {
                GenerateCode(arg1, arg2.Left, nullable);
            }
        }
    }

    private void GenerateCode(SourceProductionContext productionContext,
        (ClassDeclarationSyntax ClassDeclarationSyntax, INamedTypeSymbol NamedTypeSymbol, List<IPropertySymbol>
            PropertySymbols) args, bool nullable)
    {
        var (_, declaredSymbol, propertySymbols) = args;
        if (!propertySymbols.Any())
        {
            return;
        }

        var ns = declaredSymbol.ContainingNamespace.ToDisplayString();
        var className = declaredSymbol.Name;
        var builder = new StringBuilder();
        builder.AppendLine($"partial class {className}");
        builder.AppendLine("{");
        foreach (var propertySymbol in propertySymbols)
        {
            builder.AppendLine(
                $"{propertySymbol.DeclaredAccessibility.ToString().ToLower()} partial {propertySymbol.Type.ToDisplayString()} {propertySymbol.Name}");
            builder.AppendLine("{");
            builder.AppendLine("get => field;");
            builder.AppendLine(
                $@"set{{
var oldValue=field;
global::ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this,ref field,value);
if (!global::System.Collections.Generic.EqualityComparer<{propertySymbol.Type.ToDisplayString()}>.Default.Equals(oldValue, value)){{
On{propertySymbol.Name}Changed();
On{propertySymbol.Name}Changed(value);
On{propertySymbol.Name}Changed(oldValue,value);
}}
}}");
            builder.AppendLine("}");
            builder.AppendLine(
                $"partial void On{propertySymbol.Name}Changed();");
            builder.AppendLine(
                $"partial void On{propertySymbol.Name}Changed({propertySymbol.Type.ToDisplayString()} value);");
            builder.AppendLine(
                $"partial void On{propertySymbol.Name}Changed({propertySymbol.Type.ToDisplayString()} oldValue,{propertySymbol.Type.ToDisplayString()} newValue);");
        }

        builder.AppendLine("}");
        productionContext.AddSource($"{declaredSymbol.ToDisplayString()}.ObservableProperty.g.cs",
            SyntaxFactory.ParseMemberDeclaration(builder.ToString())!.As<ClassDeclarationSyntax>()!.Output(ns, nullable));
    }
}