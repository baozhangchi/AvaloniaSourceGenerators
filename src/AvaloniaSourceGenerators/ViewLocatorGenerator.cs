using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaSourceGenerators;

[Generator(LanguageNames.CSharp)]
internal class ViewLocatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var typesProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax { Parent: BaseNamespaceDeclarationSyntax },
            static (generatorExecutionContext, _) =>
            {
                var classDeclarationSyntax = (ClassDeclarationSyntax)generatorExecutionContext.Node;
                var declaredSymbol =
                    (INamedTypeSymbol)ModelExtensions.GetDeclaredSymbol(generatorExecutionContext.SemanticModel,
                        classDeclarationSyntax)!;
                var fullName = declaredSymbol.ToDisplayString();
                return declaredSymbol.IsInterface("Avalonia.Controls.Templates.IDataTemplate") &&
                       classDeclarationSyntax.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword))
                    ? (declaredSymbol, classDeclarationSyntax, Kind.ViewLocator)
                    : declaredSymbol.IsType("Avalonia.StyledElement")
                        ? (declaredSymbol, classDeclarationSyntax, Kind.View)
                        : (declaredSymbol, classDeclarationSyntax, Kind.ViewModel);
            }).Collect();

        context.RegisterSourceOutput(typesProvider.Combine(context.CompilationProvider), GenerateCode);
    }

    private void GenerateCode(SourceProductionContext arg1,
        (ImmutableArray<(INamedTypeSymbol declaredSymbol, ClassDeclarationSyntax classDeclarationSyntax, Kind)> Left,
            Compilation Right) arg2)
    {
        if (arg2.Right is CSharpCompilation compilation)
        {
            var nullable = compilation.Options.NullableContextOptions == NullableContextOptions.Enable;
            GenerateCode(arg1, arg2.Left, nullable);
        }
    }

    private void GenerateCode(SourceProductionContext sourceProductionContext,
        ImmutableArray<(INamedTypeSymbol declaredSymbol, ClassDeclarationSyntax classDeclarationSyntax, Kind kind)>
            args, bool nullable)
    {
        if (!args.Any(x => x.kind == Kind.ViewLocator))
        {
            return;
        }

        var viewInfos = args.Where(x => x.kind == Kind.View).ToList();
        var viewModelInfos = args.Where(x => x.kind == Kind.ViewModel).ToList();
        foreach (var (declaredSymbol, declarationSyntax, kind) in args.Where(x => x.kind == Kind.ViewLocator))
        {
            var methodDeclarationSyntax = declarationSyntax.ChildNodes()
                .OfType<MethodDeclarationSyntax>().FirstOrDefault(x =>
                    x.Identifier.Text == "Build" && x.ParameterList.Parameters.Count == 1 &&
                    x.ParameterList.Parameters[0].Type!.ToString() == "object?");

            if (methodDeclarationSyntax != null &&
                !methodDeclarationSyntax.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)))
            {
                continue;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"partial class {declaredSymbol.Name}{{");

            builder.AppendLine(
                $"public static global::Avalonia.Controls.Templates.IDataTemplate Default {{get;}}=new global::{declaredSymbol.ToDisplayString()}();");

            if (methodDeclarationSyntax == null)
            {
                if(nullable)
                {
                    builder.AppendLine("public global::Avalonia.Controls.Control? Build(object? param)");
                }
                else
                {
                    builder.AppendLine("public global::Avalonia.Controls.Control Build(object param)");
                }
            }
            else
            {
                if(nullable)
                {
                    builder.AppendLine("public partial global::Avalonia.Controls.Control? Build(object? param)");
                }
                else
                {
                    builder.AppendLine("public partial global::Avalonia.Controls.Control Build(object param)");
                }
            }

            builder.AppendLine("{");
            builder.AppendLine("if (param is null)");
            builder.AppendLine("return null;");
            builder.AppendLine("switch (param)");
            builder.AppendLine("{");
            foreach (var (viewModelTypeSymbol, _, _) in viewModelInfos)
            {
                var (viewTypeSymbol, _, _) =
                    viewInfos.FirstOrDefault(x =>
                        x.declaredSymbol.ToDisplayString() ==
                        viewModelTypeSymbol.ToDisplayString().Replace("ViewModel", "View"));
                if (viewTypeSymbol != null)
                {
                    builder.AppendLine($"case {viewModelTypeSymbol.ToDisplayString()}:");
                    builder.AppendLine($"return new {viewTypeSymbol.ToDisplayString()}() {{ DataContext = param }};");
                }
            }

            builder.AppendLine("default:");
            builder.AppendLine(
                @"throw new global::System.Exception(""Not Found View Of Type: "" + param.GetType().FullName);");
            builder.AppendLine("}");

            builder.AppendLine("}");

            builder.AppendLine("}");

            sourceProductionContext.AddSource($"{declaredSymbol.Name}_ViewLocator.g.cs",
                SyntaxFactory.ParseMemberDeclaration(builder.ToString())!.As<ClassDeclarationSyntax>()!.Output(
                    declaredSymbol.ContainingNamespace.ToDisplayString(), nullable));
        }
    }

    private enum Kind
    {
        ViewLocator,
        View,
        ViewModel
    }
}