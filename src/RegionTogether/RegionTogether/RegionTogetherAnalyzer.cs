using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Text;

namespace RegionTogether
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RegionTogetherAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "RegionTogether";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Comment";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Method, SymbolKind.Property);
        }

        private static void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            var symbol = context.Symbol;
            var symbolLocation = context.Symbol.Locations.First();
            var root = symbolLocation.SourceTree.GetRoot();
            if (!(root.FindNode(symbolLocation.SourceSpan) is MemberDeclarationSyntax declaration)) return;

            var regionDirective = GetLeadingRegionAndName(declaration, out var name);
            if (regionDirective == null || name != CreateRegionName(symbol))
            {
                var diagnostic = Diagnostic.Create(Rule, symbolLocation, symbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }

        public static SyntaxTrivia? GetLeadingRegionAndName(MemberDeclarationSyntax declaration, out string name)
        {
            name = null;

            // メソッドまたはプロパティの前に#regionがあるか(#endregionがあったらそれより前の#regionは無効)
            SyntaxTrivia? regionDirective = null;
            foreach (var trivia in declaration.GetFirstToken().LeadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia)) regionDirective = trivia;
                else if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia)) regionDirective = null;
            }
            if (regionDirective == null) return null;

            // メソッドまたはプロパティの後に#endregionがあるか(#regionがあったらそれより後ろの#endregionは無効)
            var nextNodeOrToken = declaration.Parent.ChildNodesAndTokens().SkipWhile(x => x != declaration).Skip(1).First();
            var nextToken = nextNodeOrToken.IsToken 
                ? nextNodeOrToken.AsToken() 
                : nextNodeOrToken.AsNode().DescendantTokens().First();
            SyntaxTrivia? endRegionDirective = null;
            foreach (var trivia in nextToken.LeadingTrivia.Reverse())
            {
                if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia)) endRegionDirective = trivia;
                else if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia)) endRegionDirective = null;
            }
            if (endRegionDirective == null) return null;

            // #region hogehoge のhogehogeを取得
            var endOfDirective = regionDirective.Value.GetStructure().ChildTokens().Cast<SyntaxToken?>().FirstOrDefault(x => x.Value.IsKind(SyntaxKind.EndOfDirectiveToken));
            if (endOfDirective != null)
            {
                var nameTrivia = endOfDirective.Value.LeadingTrivia.Cast<SyntaxTrivia?>().FirstOrDefault(x => x.Value.IsKind(SyntaxKind.PreprocessingMessageTrivia));
                name = nameTrivia != null ? nameTrivia.ToString() : "";
            }

            return regionDirective;
        }

        public static string CreateRegionName(ISymbol symbol)
            => GetAccessibilitySymbol(symbol.DeclaredAccessibility) + symbol.Name;

        public static string GetAccessibilitySymbol(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public:
                    return "+";
                case Accessibility.Protected:
                    return "#";
                case Accessibility.Internal:
                    return "~";
                case Accessibility.ProtectedOrInternal:
                    return "#~";
                default:
                    return "-";
            }
        }
    }
}
