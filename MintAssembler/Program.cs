using System.CommandLine;
using Mint;
using Mint.AstNodes;
using Mint.Semantics;

namespace MintAssembler
{
    internal class Program
    {
        static bool isVerbose;

        static void Main(string[] args)
        {
            RootCommand root = new("Mint to bytecode CLI tool");

            Command compileCom = new("compile", "Compile a .mint file");
            Argument<FileInfo> compileFileArg = new("file")
            {
                Description = "The file to compile"
            };
            Option<bool> verbose = new("--verbose", ["-v"])
            {
                Description = "Show more info"
            };
            compileCom.Arguments.Add(compileFileArg);
            compileCom.Options.Add(verbose);
            compileCom.SetAction(result =>
            {
                FileInfo? file = result.GetValue(compileFileArg);
                if (file is null)
                {
                    Console.Error.WriteLine("No file to compile provided.");
                    return;
                }
                Compile(file, result.GetValue(verbose));
            });

            Command insertCom = new("insert", "Insert a compiled module")

            root.Subcommands.Add(compileCom);

            ParseResult result = root.Parse(args);
            result.Invoke();
        }

        static void Compile(FileInfo inputFile, bool isVerbose)
        {
            if (isVerbose)
                Console.WriteLine("Compile command started, checking for input file...");

            if (!inputFile.Exists)
            {
                Console.Error.WriteLine($"{inputFile.FullName} does not exist");
                return;
            }

            if (isVerbose)
                Console.WriteLine("Input file found! Reading and building tokens from content...");

            List<Token> tokens = new Lexer(File.ReadAllText(inputFile.FullName)).Tokenize();

            if (isVerbose)
                Console.WriteLine($"Made {tokens.Count} tokens!\nBuilding the syntax tree...");

            ModuleNode module = new Parser(tokens).Parse();

            if (isVerbose)
                Console.WriteLine($"Building the syntax tree has finished. Found {module.Classes.Count} classes.\n" +
                    "Resolving types...");

            SemanticResult result = new SemanticAnalyser(new VersionRules([0x00, 0x02])).Analyse(module);


            Console.WriteLine($"Errors : {result.Errors.Count}\nExprTypes : {result.ExprTypes.Count}");

            foreach (SemanticError error in result.Errors)
                Console.WriteLine(error);
        }
    }
}
