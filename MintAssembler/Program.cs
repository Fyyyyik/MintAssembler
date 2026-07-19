using System.CommandLine;
using System.Reflection.Metadata.Ecma335;
using KirbyLib.IO;
using KirbyLib.Mint;
using Mint;
using Mint.Archive;
using Mint.AstNodes;
using Mint.CodeGenerators;
using Mint.Semantics;
using OneOf;

namespace MintAssembler
{
    internal class Program
    {
        private static bool _verbose;

        static void Main(string[] args)
        {
            RootCommand root = new("Mint to bytecode CLI tool");

            Command compileCom = new("compile", "Compile a .mint file");
            Argument<FileInfo> compileFileArg = new("file")
            {
                Description = "The file to compile."
            };
            Argument<string> version = new("version")
            {
                Description = "The target version of the module."
            };
            Option<bool> verbose = new("--verbose", ["-v"])
            {
                Description = "Show more info."
            };
            Option<FileInfo> outputPath = new("--output", ["-o"])
            {
                Description = "Specify where the compiled binary should be created. If the path points to a Mint archive the module will be added to it."
            };
            Option<List<FileInfo>> archives = new("--archive", ["-a"])
            {
                Description = "The Mint archives to use as context.",
                AllowMultipleArgumentsPerToken = true
            };
            compileCom.Arguments.Add(compileFileArg);
            compileCom.Arguments.Add(version);
            compileCom.Options.Add(verbose);
            compileCom.Options.Add(outputPath);
            compileCom.Options.Add(archives);
            compileCom.SetAction(result =>
            {
                FileInfo? file = result.GetValue(compileFileArg);
                if (file is null)
                {
                    Console.Error.WriteLine("No file to compile was provided.");
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
                    Version = ver,
                    IsVerbose = result.GetValue(verbose),
                    Output = result.GetValue(outputPath),
                    Archives = result.GetValue(archives)
                };
                Compile(options);
            });

            root.Subcommands.Add(compileCom);

            ParseResult result = root.Parse(args);
            result.Invoke();
        }

        static void Compile(CompileOptions options)
        {
            _verbose = options.IsVerbose;

            VersionRules rules = VersionRules.GetRules(options.Version);

            WriteVerbose("Compile command started, checking for input file...");

            if (!options.InputFile.Exists)
            {
                Console.Error.WriteLine($"'{options.InputFile.FullName}' does not exist");
                return;
            }

            WriteVerbose("Input file found! Reading and building tokens from content...");

            List<Token> tokens = new Lexer(File.ReadAllText(options.InputFile.FullName)).Tokenize();

            WriteVerbose($"Made {tokens.Count} tokens!\nValidating tokens...");

            rules.ValidateTokens(tokens);

            WriteVerbose("All tokens are valid. Building the initial syntax tree...");

            ModuleNode module = new Parser(tokens, options.InputFile).Parse();

            if (options.Archives != null)
            {
                WriteVerbose($"Initial syntax tree building has finished with {module.Objects.Count} objects.\n" +
                    $"User provided {options.Archives.Count} Mint archives.");

                foreach (FileInfo file in options.Archives)
                {
                    WriteVerbose($"Checking if a file exists at '{file.FullName}'...");
                    if (!file.Exists)
                    {
                        Console.Error.WriteLine($"'{file.FullName}' does not exist. Ignoring...");
                        continue;
                    }

                    WriteVerbose("File found! Reading archive...");
                    OneOf<Archive, ArchiveRtDL> archive;
                    try
                    {
                        using (FileStream stream = new(file.FullName, FileMode.Open, FileAccess.Read))
                        using (EndianBinaryReader reader = new(stream))
                            if (options.Version == "0.2")
                            {
                                WriteVerbose("Target version is 0.2, using KirbyLib.Mint.ArchiveRtDL...");
                                archive = new ArchiveRtDL(reader);
                            }
                            else
                            {
                                WriteVerbose("Target version is not 0.2, using KirbyLib.Mint.Archive...");
                                archive = new Archive(reader);
                            }
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"Exception when reading '{file.FullName}' : {e.Message}");
                        continue;
                    }

                    WriteVerbose("Successfully read archive! Verifying version...");
                    bool error = false;
                    archive.Switch((arch) =>
                    {
                        string ver = FormatVersion(arch.Version);
                        if (ver != options.Version)
                        {
                            Console.Error.WriteLine($"Error : expected version {options.Version} but got {ver}");
                            error = true;
                        }
                    }, (arch) =>
                    {
                        if (options.Version != "0.2")
                        {
                            Console.Error.WriteLine("Error : somehow read a RtDL archive when the target version is not 0.2");
                            error = true;
                        }
                    });

                    if (error) continue;

                    WriteVerbose("Version verified. Analysing and appending new information to the syntax tree...");
                    ArchiveAnalyser archAnalyser = new(module);
                    archive.Switch((arch) => archAnalyser.Analyse(arch), (arch) => archAnalyser.Analyse(arch));
                    WriteVerbose("Finished with this archive!");
                }
            }

            WriteVerbose($"Building the syntax tree has finished. Found {module.Objects.Count} objects.\n" +
                    "Rewriting and resolving types...");

            SemanticResult result = new SemanticAnalyser(rules).Analyse(module, out ModuleNode rewritten);

            if (options.IsVerbose || result.Errors.Count > 0)
            {
                Console.WriteLine($"Found {result.Errors.Count} errors when resolving types.");
                foreach (SemanticError error in result.Errors)
                {
                    Console.Error.WriteLine($"Node : {error.Node}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine(error.Message + '\n');
                    Console.ResetColor();
                }
                if (result.Errors.Count > 0)
                {
                    Console.WriteLine("Compiling has stopped. There were errors found during semantic analysis.");
                    return;
                }
            }

            WriteVerbose("Semantic analysis completed without issue.\nGenerating the code for Mint version 0.2.0.0 ...");

            ModuleRtDL compiled = new V0_2Generator(result).GenerateRtDL(rewritten);

            if (options.Output != null && options.Output.Exists)
            {
                WriteVerbose($"Path '{options.Output}' points to a file. Replacing the module in the Mint archive...");

                if (options.Version == "0.2")
                {
                    ArchiveRtDL outArchive;
                    using (FileStream stream = new FileStream(options.Output.FullName, FileMode.Open, FileAccess.Read))
                    using (EndianBinaryReader reader = new(stream))
                        outArchive = new(reader);

                    if (outArchive.ModuleExists(compiled.Name))
                    {
                        ModuleRtDL ogModule = outArchive.GetModule(compiled.Name);
                        outArchive.Modules.Remove(ogModule);
                    }
                    outArchive.Modules.Add(compiled);

                    using (FileStream stream1 = new(options.Output.FullName, FileMode.Open, FileAccess.Write))
                    using (EndianBinaryWriter writer = new(stream1))
                        outArchive.Write(writer);
                }

                WriteVerbose("Replaced!");
            }
            else
            {
                string output = options.Output == null ?
                    $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{compiled.Name}.bin" :
                    options.Output.FullName;

                WriteVerbose($"Finished generating the Mint module!\nSaving it to '{output}'...");

                using (FileStream stream = new(output, FileMode.Create, FileAccess.Write))
                using (EndianBinaryWriter writer = new(stream))
                    compiled.Write(writer);
            }

            WriteVerbose("Finished writing.\nThe compiler is finished.");
        }

        static void WriteVerbose(string message)
        {
            if (_verbose)
                Console.WriteLine(message);
        }

        static string FormatVersion(byte[] ver)
        {
            bool read = false;
            string formatted = "";
            for (int i = ver.Length - 1; i >= 0; i--)
            {
                if (!read && ver[i] != 0)
                {
                    read = true;
                    formatted = ver[i].ToString();
                    continue;
                }
                if (read)
                    formatted = $"{ver[i]}.";
            }
            return formatted;
        }
    }
}
