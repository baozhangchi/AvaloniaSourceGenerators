using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avalonia.InternalCheat;

[Generator(LanguageNames.CSharp)]
internal class ReactiveCommandGenerator : IIncrementalGenerator
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
                var methodSymbols = classDeclarationSyntax.ChildNodes().OfType<MethodDeclarationSyntax>()
                    .Select(x => generatorExecutionContext.SemanticModel.GetDeclaredSymbol(x)!)
                    .Where(x => x.GetAttributes().Any(a =>
                        a.AttributeClass!.ToDisplayString() == typeof(ReactiveCommandAttribute).FullName))
                    .ToList();
                return
                (
                    ClassDeclarationSyntax: classDeclarationSyntax,
                    NamedTypeSymbol: declaredSymbol,
                    MethodSymbols: methodSymbols
                );
            });
        context.RegisterSourceOutput(typesProvider.Combine(context.CompilationProvider), GenerateCode);
    }

    private void GenerateCode(SourceProductionContext arg1,
        ((ClassDeclarationSyntax ClassDeclarationSyntax, INamedTypeSymbol NamedTypeSymbol, List<IMethodSymbol>
            MethodSymbols) Left, Compilation Right) arg2)
    {
        if (arg2.Right is CSharpCompilation compilation)
        {
            var nullable = compilation.Options.NullableContextOptions == NullableContextOptions.Enable;
            GenerateCode(arg1, arg2.Left, nullable);
        }
    }

    private void GenerateCode(SourceProductionContext productionContext,
        (ClassDeclarationSyntax ClassDeclarationSyntax, INamedTypeSymbol NamedTypeSymbol, List<IMethodSymbol>
            MethodSymbols) args, bool nullable)
    {
        var (_, declaredSymbol, methodSymbols) = args;
        if (!methodSymbols.Any())
        {
            return;
        }

        var builder = new StringBuilder();
        try
        {
            var ns = declaredSymbol.ContainingNamespace.ToString();
            var className = declaredSymbol.Name;
            builder.AppendLine($"partial class {className}{{");
            foreach (var methodSymbol in methodSymbols)
            {
                var attribute = methodSymbol.GetAttributes().First(x =>
                    x.AttributeClass!.ToDisplayString() == typeof(ReactiveCommandAttribute).FullName);
                var parameterTypes = methodSymbol.Parameters.Select(x => x.Type.ToDisplayString()).ToList();
                if (parameterTypes.Count > 1)
                {
                    throw new Exception($"方法{methodSymbol.Name}的参数最多只能有一个");
                }

                var returnType = methodSymbol.ReturnType.ToDisplayString();
                var parameterType = parameterTypes.Any() ? parameterTypes.First() : "global::System.Reactive.Unit";
                if (returnType == "void")
                {
                    returnType = "global::System.Reactive.Unit";
                }

                if (attribute.NamedArguments.Length > 0)
                {
                    var canExecute = attribute.NamedArguments
                        .FirstOrDefault(x => x.Key == nameof(ReactiveCommandAttribute.CanExecute)).Value.Value
                        ?.ToString();
                    if (!string.IsNullOrWhiteSpace(canExecute))
                    {
                        var property = declaredSymbol.GetMembers(canExecute!).OfType<IPropertySymbol>()
                            .FirstOrDefault();
                        if (property != null)
                        {
                            if (property.Type.ToDisplayString() != "System.IObservable<bool>")
                            {
                                throw new Exception($"属性{property.Name}的类型必须为：IObservable<bool>");
                            }

                            if (parameterType != "global::System.Reactive.Unit")
                            {
                                builder.AppendLine(
                                    $@"public global::ReactiveUI.ReactiveCommand<{parameterType}, {returnType}> {methodSymbol.Name}Command => global::ReactiveUI.ReactiveCommand.Create<{parameterType}, {returnType}>({methodSymbol.Name},{property.Name});");
                            }
                            else
                            {
                                builder.AppendLine(
                                    $@"public global::ReactiveUI.ReactiveCommand<{parameterType}, {returnType}> {methodSymbol.Name}Command => global::ReactiveUI.ReactiveCommand.Create({methodSymbol.Name},{property.Name});");
                            }
                        }
                        else
                        {
                            var field = declaredSymbol.GetMembers(canExecute!).OfType<IFieldSymbol>().FirstOrDefault();
                            if (field != null)
                            {
                                if (field.Type.ToDisplayString() != "System.IObservable<bool>")
                                {
                                    throw new Exception($"字段{field.Name}的类型必须为：IObservable<bool>");
                                }

                                if (parameterType != "global::System.Reactive.Unit")
                                {
                                    builder.AppendLine(
                                        $@"public global::ReactiveUI.ReactiveCommand<{parameterType}, {returnType}> {methodSymbol.Name}Command => global::ReactiveUI.ReactiveCommand.Create<{parameterType}, {returnType}>({methodSymbol.Name},{field.Name});");
                                }
                                else
                                {
                                    builder.AppendLine(
                                        $@"public global::ReactiveUI.ReactiveCommand<{parameterType}, {returnType}> {methodSymbol.Name}Command => global::ReactiveUI.ReactiveCommand.Create({methodSymbol.Name},{field.Name});");
                                }
                            }
                            else
                            {
                                throw new Exception($"没有找到名称为{canExecute}的属性或字段");
                            }
                        }
                    }
                }
                else
                {
                    if (parameterType != "global::System.Reactive.Unit")
                    {
                        builder.AppendLine(
                            $@"public global::ReactiveUI.ReactiveCommand<{parameterType}, {returnType}> {methodSymbol.Name}Command => global::ReactiveUI.ReactiveCommand.Create<{parameterType}, {returnType}>({methodSymbol.Name});");
                    }
                    else
                    {
                        builder.AppendLine(
                            $@"public global::ReactiveUI.ReactiveCommand<{parameterType}, {returnType}> {methodSymbol.Name}Command => global::ReactiveUI.ReactiveCommand.Create({methodSymbol.Name});");
                    }
                }
            }

            builder.AppendLine("}");

            productionContext.AddSource($"{declaredSymbol.ToDisplayString()}.ReactiveCommand.g.cs",
                SyntaxFactory.ParseMemberDeclaration(builder.ToString())!.As<ClassDeclarationSyntax>()!.Output(ns,
                    nullable));
        }
        catch (Exception e)
        {
            // 将异常转换为诊断信息
            var errorDiagnostic = Diagnostic.Create(
                new DiagnosticDescriptor(
                    "RCGEN001",
                    "生成器异常",
                    $"生成ReactiveCommand失败: {e.Message}",
                    nameof(ReactiveCommandGenerator),
                    DiagnosticSeverity.Error,
                    true),
                null);

            productionContext.ReportDiagnostic(errorDiagnostic);
        }
    }
}