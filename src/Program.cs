using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

#if DEBUG
await Cmd("../meta/metadata.json", "../meta", "../meta/symbols.json");
#else
await Cmd(args[0], args[1], args[2]);
#endif

static async Task Cmd(string json, string source, string output)
{
    Meta meta = await ReadMeta(json);
    if (!Directory.Exists(source))
    {
        throw new ArgumentException("Directory `{source}` doesnt exist.");
    }
    List<Link> links = new();
    await foreach (var l in GetRepoTypeLinks(meta, source))
    {
        links.Add(l);
    }
    using FileStream fs = File.Create(output);
    await JsonSerializer.SerializeAsync(fs, links);
}

static async IAsyncEnumerable<Link> GetRepoTypeLinks(Meta meta, string source)
{
    string linkBase = meta.link.documents.Values.First();
    foreach (Doc doc in meta.docs)
    {
        string path = Path.Combine(source, doc.name);
        List<Link> links = new();
        await foreach (Link l in GetTypeLinks(doc, await File.ReadAllTextAsync(path)))
        {
            yield return new(l.typename, Regex.Replace(linkBase, @"\*", l.link), l.start, l.end);
        }
    }
}

static async IAsyncEnumerable<Link> GetTypeLinks(Doc doc, string code)
{
    List<int> lines = GetLineOffsets(code);
    SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
    await foreach (BaseTypeDeclarationSyntax type in GetTypeSymbols(tree))
    {
        var (start, end) = GetLineNo(lines, type.FullSpan);
        yield return new(type.Identifier.ToString(), doc.name, start, end);
    }
}

static async IAsyncEnumerable<BaseTypeDeclarationSyntax> GetTypeSymbols(SyntaxTree doc)
{
    var root = (CompilationUnitSyntax)await doc.GetRootAsync();
    BaseTypeDeclarationVisitor visitor = new();
    root.Accept(visitor);
    foreach (BaseTypeDeclarationSyntax type in visitor.Nodes)
    {
        yield return type;
    }
}

static (int start, int end) GetLineNo(List<int> lines, TextSpan span)
{
    int start = -1;
    var en = lines.GetEnumerator();
    while (en.MoveNext())
    {
        if (span.Start >= en.Current)
        {
            break;
        }
        start = en.Current;
    }
    int end = start;
    while (en.MoveNext())
    {
        if (span.Start >= en.Current)
        {
            break;
        }
        end = en.Current;
    }
    return (start, end);
}

static List<int> GetLineOffsets(string code)
{
    List<int> lines = new();
    char nl = Environment.NewLine[0];
    for (int o = 0; o < code.Length; o++)
    {
        char c = code[o];
        if (c == nl)
        {
            lines.Add(o);
        }
    }
    return lines;
}

static async Task<Meta> ReadMeta(string path)
{
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<Meta>(fs);
}

readonly record struct Meta(SourceLink link, List<Doc> docs);

readonly record struct Doc(string name, Guid lang, Guid algo, byte[] hash);

readonly record struct SourceLink(Dictionary<string, string> documents);

record struct Link(string typename, string link, int start, int end);

sealed class BaseTypeDeclarationVisitor : CSharpSyntaxVisitor
{
    public List<BaseTypeDeclarationSyntax> Nodes { get; } = new();

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        Nodes.Add(node);
        base.VisitClassDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        Nodes.Add(node);
        base.VisitEnumDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        Nodes.Add(node);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        Nodes.Add(node);
        base.VisitStructDeclaration(node);
    }
}
