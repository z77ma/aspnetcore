// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.AspNetCore.Analyzers.AsyncVoidInMethodDeclaration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public partial class AsyncVoidInMethodDeclarationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create<DiagnosticDescriptor>(new[]
        {
            DiagnosticDescriptors.AvoidAsyncVoidInMethodDeclaration
        });

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStartContext =>
        {
            if (!WellKnownTypes.TryCreate(compilationStartContext.Compilation, out var wellKnownTypes))
            {
                Debug.Fail("One or more types could not be found. This usually means you are bad at spelling C# type names.");
                return;
            }

            InitializeWorker(compilationStartContext, wellKnownTypes);
        });
    }

    private static void InitializeWorker(CompilationStartAnalysisContext compilationStartContext, WellKnownTypes wellKnownTypes)
    {
        compilationStartContext.RegisterSyntaxNodeAction(classContext =>
        {
            if (classContext.Node is not ClassDeclarationSyntax classDeclaration)
            {
                return;
            }

            var classSymbol = GetDeclaredSymbol<ITypeSymbol>(classContext, classDeclaration);

            if (IsController(classSymbol, wellKnownTypes)
                || IsSignalRHub(classSymbol, wellKnownTypes)
                || IsMvcFilter(classSymbol, wellKnownTypes))
            {
                // scan all methods in class
                CheckMembers(classDeclaration.Members, wellKnownTypes, classContext, null);
            }
            else if (IsRazorPage(classSymbol, wellKnownTypes))
            {
                // only search for methods that follow a pattern: 'On + HttpMethodName'
                CheckMembers(classDeclaration.Members, wellKnownTypes, classContext, IsRazorPageHandlerMethod);
            }
        }, SyntaxKind.ClassDeclaration);
    }

    private static T? GetDeclaredSymbol<T>(SyntaxNodeAnalysisContext context, SyntaxNode syntax) where T : class
    {
        return context.SemanticModel.GetDeclaredSymbol(syntax, context.CancellationToken) as T;
    }

    private static void CheckMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        WellKnownTypes wellKnownTypes,
        SyntaxNodeAnalysisContext classContext,
        Func<IMethodSymbol?, WellKnownTypes, bool>? additionalMethodConstraint)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] is not MethodDeclarationSyntax methodDeclarationSyntax)
            {
                continue;
            }

            var methodSymbol = GetDeclaredSymbol<IMethodSymbol>(classContext, methodDeclarationSyntax);
            // check if there is an additional filter for a method and the method conforms it
            if (additionalMethodConstraint != null && !additionalMethodConstraint(methodSymbol, wellKnownTypes))
            {
                continue;
            }

            if (methodSymbol != null && methodSymbol.IsAsync && methodSymbol.ReturnsVoid)
            {
                var diagnosticSpan = new TextSpan(
                    methodDeclarationSyntax.Modifiers.Last().FullSpan.Start,
                    methodDeclarationSyntax.ReturnType.FullSpan.End - methodDeclarationSyntax.Modifiers.Last().FullSpan.Start);

                var location = Location.Create(methodDeclarationSyntax.SyntaxTree, diagnosticSpan);
                var diagnostic = Diagnostic.Create(DiagnosticDescriptors.AvoidAsyncVoidInMethodDeclaration, location);

                classContext.ReportDiagnostic(diagnostic);
            }
        }
    }
}
