using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace YARA
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(YARACodeFixProvider)), Shared]
    public class YARACodeFixProvider : CodeFixProvider
    {

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(YARAAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
             
            var memberAccessNode = root.FindToken(diagnosticSpan.Start).Parent.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();

            var semanticmodel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            var method = semanticmodel.GetEnclosingSymbol(memberAccessNode.SpanStart) as IMethodSymbol;


            if (method != null && method.IsAsync)
            {
                var invokeMethod = semanticmodel.GetSymbolInfo(memberAccessNode).Symbol as IMethodSymbol;

                if (invokeMethod != null)
                {
                    var invocation = memberAccessNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();

                    if (!memberAccessNode.Name.Identifier.Text.Contains("Async"))
                    {
                        var name = memberAccessNode.Name.Identifier.Text;

                        // Register a code action that will invoke the fix.
                        context.RegisterCodeFix(
                            new CodeActionChangetoAwaitAsync("Swap with Async",
                                c => ChangetoAwaitAsync(context.Document, invocation, name, c)),
                            diagnostic);
                        return;
                    }
                }
            }
        }

        private async Task<Document> ChangetoAwaitAsync(Document document, InvocationExpressionSyntax invocation, string name, CancellationToken cancellationToken)
        {
            SyntaxNode oldExpression = invocation;
            SyntaxNode newExpression = null;


            if (!name.Contains("Async"))
            {
                var oldToken = invocation.DescendantTokens().First(z => z.Text == name);
                 
                var newToken = SyntaxFactory.Identifier(oldToken.LeadingTrivia,SyntaxKind.IdentifierToken,name + "Async", name + "Async", oldToken.TrailingTrivia);

                var tempInvocation = invocation.ReplaceToken(oldToken, newToken);
                newExpression = SyntaxFactory.AwaitExpression(tempInvocation).WithAdditionalAnnotations(Formatter.Annotation);
            }

            var oldroot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newroot = oldroot.ReplaceNode(oldExpression, newExpression);

            var newDocument = document.WithSyntaxRoot(newroot);

            return newDocument;
        }
    }


    public class CodeActionChangetoAwaitAsync : CodeAction
    {
        private Func<CancellationToken, Task<Document>> _generateDocument;
        private string _title;

        public CodeActionChangetoAwaitAsync(string title, Func<CancellationToken, Task<Document>> generateDocument)
        {
            _title = title;
            _generateDocument = generateDocument;
        }

        public override string Title { get { return _title; } }

        protected override Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
        {
            return _generateDocument(cancellationToken);
        }

        public override string EquivalenceKey
        {
            get
            {
                return nameof(CodeActionChangetoAwaitAsync);
            }
        }
    }

}