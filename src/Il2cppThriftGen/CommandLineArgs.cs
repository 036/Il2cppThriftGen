using CommandLine;
using System.IO;

namespace Il2cppThriftGen
{
    public class CommandLineArgs
    {
        [Option("il2pp-file", Required = true, HelpText = "Specify path to the native il2cpp file")]
        public string Il2cppFile { get; set; } = null!;

        [Option("metadata-file", Required = true, HelpText = "Specify path to the unity metadata file")]
        public string MetadataFile { get; set; }

        [Option("output-root", HelpText = "Root directory to output to. Defaults to il2cppthiftgen_out in the current working directory")]
        public string OutputRootDir { get; set; } = Path.GetFullPath("cpp2il_out");
    }
}
