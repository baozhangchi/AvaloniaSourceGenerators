using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avalonia.InternalCheat;

[Generator(LanguageNames.CSharp)]
internal class StyledPropertyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var typesProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax
            {
                Parent: BaseNamespaceDeclarationSyntax,
            },
            static (generatorExecutionContext, _) =>
            {
                var classDeclarationSyntax = (ClassDeclarationSyntax)generatorExecutionContext.Node;
                var declaredSymbol =
                    generatorExecutionContext.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax)!;
                var propertySymbols = classDeclarationSyntax.ChildNodes().OfType<PropertyDeclarationSyntax>()
                    .Select(x => generatorExecutionContext.SemanticModel.GetDeclaredSymbol(x)!)
                    .Where(x => x.GetAttributes().Any(a =>
                        a.AttributeClass!.ToDisplayString() == typeof(StyledPropertyAttribute).FullName))
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
            GenerateCode(arg1, arg2.Left, nullable);
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

        var ns = declaredSymbol.ContainingNamespace.ToString();
        var className = declaredSymbol.Name;
        var builder = new StringBuilder();
        builder.AppendLine($"partial class {className}");
        builder.AppendLine("{");
        foreach (var propertySymbol in propertySymbols)
        {
            builder.AppendLine(
                $"public static readonly global::Avalonia.StyledProperty<{propertySymbol.Type.ToDisplayString()}> {propertySymbol.Name}Property = global::Avalonia.AvaloniaProperty.Register<{declaredSymbol.ToDisplayString()}, {propertySymbol.Type.ToDisplayString()}>(nameof({propertySymbol.Name}));");
            builder.AppendLine(
                $"{propertySymbol.DeclaredAccessibility.ToString().ToLower()} partial {propertySymbol.Type.ToDisplayString()} {propertySymbol.Name}");
            builder.AppendLine("{");
            builder.AppendLine($"get => GetValue({propertySymbol.Name}Property);");
            builder.AppendLine($"set => SetValue({propertySymbol.Name}Property, value);");
            builder.AppendLine("}");
        }

        builder.AppendLine("}");
        productionContext.AddSource($"{declaredSymbol.ToDisplayString()}.StyledProperty.g.cs",
            SyntaxFactory.ParseMemberDeclaration(builder.ToString())!.As<ClassDeclarationSyntax>()!.Output(ns, nullable));
    }
}