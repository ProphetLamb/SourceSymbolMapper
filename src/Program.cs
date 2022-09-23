using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        string code = await File.ReadAllTextAsync(path);
        List<int> lines = GetLineOffsets(code);
        await foreach (BaseTypeDeclarationSyntax type in GetTypeSymbols(CSharpSyntaxTree.ParseText(code)))
        {
            var (start, end) = GetLineNo(lines, type.FullSpan);
            yield return new(GetFullTypeName(type), Regex.Replace(linkBase, @"\*", doc.name), doc.name, start, end);
        }
    }
}

static async IAsyncEnumerable<BaseTypeDeclarationSyntax> GetTypeSymbols(SyntaxTree doc)
{
    CompilationUnitSyntax root = (CompilationUnitSyntax)await doc.GetRootAsync();
    BaseTypeDeclarationVisitor visitor = new();
    root.Accept(visitor);
    foreach (BaseTypeDeclarationSyntax type in visitor.Nodes)
    {
        Console.WriteLine(type.Identifier);
        yield return type;
    }
}

static string GetFullTypeName(BaseTypeDeclarationSyntax type)
{
    StringBuilder sb = GetTypeNameHierarchy(type).Reverse().Aggregate(new StringBuilder(), (sb, n) => sb.Append(n).Append('.'));
    sb.Length -= 1;
    return sb.ToString();
}

static string GetShortTypeName(BaseTypeDeclarationSyntax type)
{
    StringBuilder sb = new();
    sb.Append(type.Identifier);
    int typeparams = type is TypeDeclarationSyntax t ? t.TypeParameterList?.Parameters.Count ?? 0 : 0;
    if (typeparams > 0)
    {
        sb.Append($"`{typeparams}");
    }
    return sb.ToString();
}

static IEnumerable<string> GetTypeNameHierarchy(SyntaxNode? node)
{
    while (node != null)
    {
        string? segment = node switch
        {
            BaseTypeDeclarationSyntax t => GetShortTypeName(t),
            BaseNamespaceDeclarationSyntax n => n.Name.ToString(),
            _ => null
        };
        if (segment is null)
        {
            Console.WriteLine($"Unable to construct heritage path. Unknown node type: {node?.GetType()}");
        }
        else
        {
            yield return segment;
        }
        node = node.Parent;
    }
}

static (int start, int end) GetLineNo(List<int> lines, TextSpan span)
{
    int line = 0;
    int start = -1;
    var en = lines.GetEnumerator();
    while (en.MoveNext())
    {
        line += 1;
        if (span.Start <= en.Current)
        {
            break;
        }
        start = line;
    }
    int end = start;
    while (en.MoveNext())
    {
        line += 1;
        if (span.End <= en.Current)
        {
            break;
        }
        end = line;
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

readonly record struct Link(string type, string link, string path, int start, int end);

sealed class BaseTypeDeclarationVisitor : CSharpSyntaxVisitor
{
    public List<BaseTypeDeclarationSyntax> Nodes { get; } = new();

    public override void DefaultVisit(SyntaxNode node)
    {
        foreach (SyntaxNode n in node.ChildNodes())
        {
            Visit(n);
        }
    }


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
