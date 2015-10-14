using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
namespace YARA
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class YARAAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "YARA001-EFAsync";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Async Helper";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            context.RegisterSyntaxNodeAction(c => AnalyzeMethodNode(c), SyntaxKind.SimpleMemberAccessExpression);

        }

        private const string _efNamespace = "System.Data.Entity";

        /// <summary>
        /// namespace is a key, value is a ist of symbols
        /// </summary>
        private static ConcurrentBag<ISymbol> _names = new ConcurrentBag<ISymbol>();

        private void AnalyzeMethodNode(SyntaxNodeAnalysisContext context)
        {

            var memberAccessNode = (MemberAccessExpressionSyntax)context.Node;


            // this gets the method that hosts 
            var method = context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart) as IMethodSymbol;

            if (method != null && method.IsAsync)
            {

                var invokeMethod = context.SemanticModel.GetSymbolInfo(context.Node).Symbol as IMethodSymbol;

                var location = memberAccessNode.Name.GetLocation();
                if (invokeMethod != null)
                {
                    var containingNamespace = invokeMethod.ContainingNamespace.ToString();

                    if (!invokeMethod.IsAsync &&
                        (containingNamespace == _efNamespace || containingNamespace == "System.Linq") &&
                        (invokeMethod.ReturnType?.BaseType?.Name ?? "") != "Task")
                    {

                        //if the containing name space is EFs namespace we can directly proceed to add all methods to our list and generate diagnostic if it has async counterpart
                        if (containingNamespace == _efNamespace)
                        {
                            AddNamespace(invokeMethod.ContainingNamespace);

                            if (HasMethod(invokeMethod.Name))
                            {
                                context.ReportDiagnostic(Diagnostic.Create(Rule, location, invokeMethod.Name));
                                return;
                            }

                        }

                        var parentExp = memberAccessNode.Expression;
                        var parentNode = parentExp.Parent.FindNode(parentExp.Span);
                        var parentIsEfType = false;

                        //if the sibling is a property ( DB.myTable ) 
                        var parentSymbolAsProperty = context.SemanticModel.GetSymbolInfo(parentNode).Symbol as IPropertySymbol;

                        if (parentSymbolAsProperty != null && (
                                                                    (parentSymbolAsProperty.Type?.ContainingNamespace?.ToString() ?? "") == _efNamespace
                                                                 || (parentSymbolAsProperty.Type?.Interfaces.Any(x => x.ContainingNamespace.ToString() == _efNamespace) ?? false)
                                                              )
                            )
                        {
                            parentIsEfType = true;
                        }

                        //if the sibling is another linq method that returns IQueriable
                        var parentSymbolAsMethod = context.SemanticModel.GetSymbolInfo(parentNode).Symbol as IMethodSymbol;

                        if (parentSymbolAsMethod != null && parentSymbolAsMethod.IsExtensionMethod && parentSymbolAsMethod.IsGenericMethod && parentSymbolAsMethod.ContainingType.ToString() == _efNamespace)
                        {
                            parentIsEfType = true;
                        }

                        if (HasMethod(invokeMethod.Name) && parentIsEfType)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(Rule, location, invokeMethod.Name));
                        }
                    }
                }
            }
        }

        private void AddNamespace(INamespaceSymbol namespaceSymbol)
        {
            if (!_names.IsEmpty) return;

            var classList = namespaceSymbol.GetMembers();

            var methodsList = classList.ToList().SelectMany(x => x.GetMembers());

            var methods = new List<ISymbol>(methodsList);
            try
            {
                _names = new ConcurrentBag<ISymbol>(methods);
            }
            catch
            {
            }
        }

        private bool HasMethod(string name)
        {
            return _names.Any(z => z.Name == name + "Async");
        }
    }
}
