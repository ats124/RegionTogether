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
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RegionTogether
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RegionTogetherCodeFixProvider)), Shared]
    public class RegionTogetherCodeFixProvider : CodeFixProvider
    {
        private const string title = "Regionブロックで囲む";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(RegionTogetherAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = (MemberDeclarationSyntax)root.FindNode(diagnosticSpan);

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => MakeRegion(context.Document, declaration, c),
                    equivalenceKey: title),
                diagnostic);
        }

        private async Task<Document> MakeRegion(Document document, MemberDeclarationSyntax declaration, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var typeSymbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);

            var newRegionDirective = SyntaxFactory.Trivia(
                SyntaxFactory.RegionDirectiveTrivia(true)
                    .WithHashToken(SyntaxFactory.Token(SyntaxKind.HashToken‌​))
                    .WithRegionKeyword(SyntaxFactory.Token(SyntaxFactory.T‌​riviaList(), SyntaxKind.RegionKeyword, SyntaxFactory.T‌​riviaList(SyntaxFactory.Whitespace(" "))))
                    .WithEndOfDirectiveToken(
                        SyntaxFactory.Token(
                            SyntaxFactory.T‌​riviaList(
                                SyntaxFact‌​ory.PreprocessingMes‌​sage(RegionTogetherAnalyzer.CreateRegionName(typeSymbol))), 
                            SyntaxKind.EndOfDirectiveToken, 
                            SyntaxFactory.TriviaList(
                                SyntaxFactory.EndOfLine("\r\n")))));

            var root = await declaration.SyntaxTree.GetRootAsync(cancellationToken);
            SyntaxNode newRoot;
            var regionDirective = RegionTogetherAnalyzer.GetLeadingRegionAndName(declaration, out var _);
            if (regionDirective != null)
            {
                newRoot = root.ReplaceTrivia(regionDirective.Value, newRegionDirective);
            }
            else
            {
                var token = declaration.GetFirstToken();
                if (token.HasLeadingTrivia)
                {
                    // メソッドやプロパティの直前にあるコメント
                    SyntaxTrivia? commentTrivia = null;
                    foreach (var trivia in token.LeadingTrivia.Reverse())
                    {
                        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                        {
                            commentTrivia = trivia;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (commentTrivia != null)
                    {
                        // メソッドやプロパティの直前にコメントがある場合はコメントの直前に差し込む
                        newRoot = root.InsertTriviaBefore(commentTrivia.Value, new[] { newRegionDirective });
                    }
                    else
                    {
                        newRoot = root.InsertTriviaAfter(token.LeadingTrivia.Last(), new[] { newRegionDirective });
                    }
                }
                else
                {
                    newRoot = root.ReplaceToken(token, token.WithLeadingTrivia(newRegionDirective));
                }

                var spanDiff = newRoot.FullSpan.End - root.FullSpan.End;
                var newDeclaration = newRoot.FindNode(new TextSpan(declaration.SpanStart + spanDiff, declaration.Span.Length));
                var nextNodeOrToken = newDeclaration.Parent.ChildNodesAndTokens().SkipWhile(x => x != newDeclaration).Skip(1).First();
                var nextToken = nextNodeOrToken.IsToken ? nextNodeOrToken.AsToken() : nextNodeOrToken.AsNode().DescendantTokens().First();
                var newEndRegionDirective = 
                    SyntaxFactory.Trivia(
                        SyntaxFactory.EndRegionDirectiveTrivia(true).WithEndOfDirectiveToken(
                            SyntaxFactory.Token(
                                SyntaxFactory.TriviaList(), 
                                SyntaxKind.EndOfDirectiveToken, 
                                SyntaxFactory.TriviaList(
                                    SyntaxFactory.EndOfLine("\r\n")))));
                if (nextToken.HasLeadingTrivia)
                {
                    newRoot = newRoot.InsertTriviaBefore(nextToken.LeadingTrivia.First(), new[] { newEndRegionDirective });
                }
                else
                {
                    newRoot = newRoot.ReplaceToken(nextToken, nextToken.WithLeadingTrivia(newEndRegionDirective));
                }
            }

            return document.WithSyntaxRoot(newRoot);
        }
    }
}