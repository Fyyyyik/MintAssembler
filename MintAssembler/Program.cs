using System.CommandLine;
using KirbyLib.IO;
using KirbyLib.Mint;
using Mint;
using Mint.AstNodes;
using Mint.CodeGenerators;
using Mint.Semantics;

namespace MintAssembler
{
    internal class Program
    {
        static void Main(string[] args)
        {
            RootCommand root = new("Mint to bytecode CLI tool");

            Command compileCom = new("compile", "Compile a .mint file");
            Argument<FileInfo> compileFileArg = new("file")
            {
                Description = "The file to compile"
            };
            Argument<string> moduleName = new("name")
            {
                Description = "The name of the compiled module"
            };
            Argument<string> version = new("version")
            {
                Description = "The target version of the module"
            };
            Option<bool> verbose = new("--verbose", ["-v"])
            {
                Description = "Show more info"
            };
            Option<string> outputPath = new("--output", ["-o"])
            {
                Description = "Specify where the compiled binary should be created"
            };
            Option<FileInfo> targetArchive = new("--archive", ["-a"])
            {
                Description = "The destination archive of the module"
            };
            compileCom.Arguments.Add(compileFileArg);
            compileCom.Arguments.Add(moduleName);
            compileCom.Arguments.Add(version);
            compileCom.Options.Add(verbose);
            compileCom.Options.Add(outputPath);
            compileCom.Options.Add(targetArchive);
            compileCom.SetAction(result =>
            {
                FileInfo? file = result.GetValue(compileFileArg);
                if (file is null)
                {
                    Console.Error.WriteLine("No file to compile was provided.");
                    return;
                }
                string? name = result.GetValue(moduleName);
                if (name is null)
                {
                    Console.Error.WriteLine("No name to the module was provided.");
                    return;
                }
                string? ver = result.GetValue(version);
                if (ver is null)
                {
                    Console.Error.WriteLine("No version to the module was provided.");
                    return;
                }

                CompileOptions options = new()
                {
                    InputFile = file,
                    ModuleName = name,
                    Version = ver,
                    IsVerbose = result.GetValue(verbose),
                    OutputPath = result.GetValue(outputPath),
                    TargetArchive = result.GetValue(targetArchive)
                };
                Compile(options);
            });

            root.Subcommands.Add(compileCom);

            ParseResult result = root.Parse(args);
            result.Invoke();
        }

        static void Compile(CompileOptions options)
        {
            if (options.IsVerbose)
                Console.WriteLine("Compile command started, checking for input file...");

            if (!options.InputFile.Exists)
            {
                Console.Error.WriteLine($"{options.InputFile.FullName} does not exist");
                return;
            }

            if (options.IsVerbose)
                Console.WriteLine("Input file found! Reading and building tokens from content...");

            List<Token> tokens = new Lexer(File.ReadAllText(options.InputFile.FullName)).Tokenize();

            if (options.IsVerbose)
                Console.WriteLine($"Made {tokens.Count} tokens!\nBuilding the syntax tree...");

            ModuleNode module = new Parser(tokens).Parse();

            if (options.IsVerbose)
                Console.WriteLine($"Building the syntax tree has finished. Found {module.Objects.Count} objects.\n" +
                    "Rewriting and resolving types...");

            string namesp = string.Join('.', options.ModuleName.Split('.')[..^1]);
            SemanticResult result = new SemanticAnalyser(new VersionRules([0x00, 0x02])).Analyse(module, namesp, out ModuleNode rewritten);

            if (options.IsVerbose || result.Errors.Count > 0)
            {
                Console.WriteLine($"Found {result.Errors.Count} errors when resolving types.");
                foreach (SemanticError error in result.Errors)
                {
                    Console.Error.WriteLine($"Node : {error.Node}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(error.Message + "\n");
                    Console.ResetColor();
                }
                if (result.Errors.Count > 0)
                {
                    Console.WriteLine("Compiling has stopped. There were errors found during semantic analysis.");
                    return;
                }
            }

            if (options.IsVerbose)
                Console.WriteLine("Semantic analysis completed without issue.\nGenerating the code for Mint version 0.2.0.0 ...");

            ModuleRtDL compiled = new V0_2Generator(result).GenerateRtDL(rewritten, options.ModuleName);

            string output = options.OutputPath == null ?
                $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{options.ModuleName}.bin" :
                options.OutputPath;

            if (options.IsVerbose)
                Console.WriteLine($"Finished generating the Mint module!\nSaving it to '{output}'...");

            using (FileStream stream = new(output, FileMode.Create, FileAccess.Write))
            using (EndianBinaryWriter writer = new(stream))
                compiled.Write(writer);

            if (options.IsVerbose)
                Console.WriteLine("Finished writing.\nThe compiler is finished.");
        }
    }
}
