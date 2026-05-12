using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AvaloniaSourceGenerators;

[Generator(LanguageNames.CSharp)]
internal class WindowManagerGenerator : IIncrementalGenerator
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
                return declaredSymbol.IsType("Avalonia.Controls.Window")
                    ? (declaredSymbol, classDeclarationSyntax, Kind.Window)
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
        if (!args.Any(x => x.kind == Kind.Window))
        {
            return;
        }

        var viewInfos = args.Where(x => x.kind == Kind.Window).ToList();
        var viewModelInfos = args.Where(x => x.kind == Kind.ViewModel).ToList();
        var builder = new StringBuilder();
        var ns = string.Empty;
        builder.AppendLine("public partial class WindowManager{");

        if (nullable)
        {
            builder.AppendLine("public global::Avalonia.Controls.Window? CreateWindow(object param)");
        }
        else
        {
            builder.AppendLine("public global::Avalonia.Controls.Window CreateWindow(object param)");
        }

        builder.AppendLine("{");
        builder.AppendLine("if (param is null)");
        builder.AppendLine("return null;");
        builder.AppendLine("switch (param)");
        builder.AppendLine("{");

        foreach (var (viewTypeSymbol, _, _) in viewInfos)
        {
            if (string.IsNullOrWhiteSpace(ns) || ns.StartsWith(viewTypeSymbol.ContainingNamespace.ToDisplayString()))
            {
                ns = viewTypeSymbol.ContainingNamespace.ToDisplayString();
            }

            var (viewModelTypeSymbol, _, _) =
                viewModelInfos.FirstOrDefault(x =>
                    x.declaredSymbol.ToDisplayString() ==
                    viewTypeSymbol.ToDisplayString().Replace("View", "ViewModel"));
            if (viewModelTypeSymbol != null)
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

        builder.AppendLine($@"public static {ns}.WindowManager Default{{get;}}=new();");
        builder.AppendLine("private WindowManager(){}");
        builder.AppendLine("}");

        sourceProductionContext.AddSource("WindowManager.g.cs",
            SyntaxFactory.ParseMemberDeclaration(builder.ToString())!.As<ClassDeclarationSyntax>()!
                .Output(ns, nullable));
    }

    private enum Kind
    {
        Window,
        ViewModel
    }
}