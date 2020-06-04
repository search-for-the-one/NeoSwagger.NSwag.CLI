using CommandLine;

namespace NeoSwagger.NSwag.CLI
{
    public class Options
    {
        [Value(0, Required = true, MetaName = "swaggerEndpoint", HelpText = "Swagger Endpoint")]
        public string SwaggerEndpoint { get; set; }

        [Option('s', "script", HelpText = "Script file")]
        public string ScriptFile { get; set; }

        [Option('q', "quit", HelpText = "Don't run interactive shell (run script and quit)")]
        public bool RunInteractiveShell { get; set; }

        [Option('p', "print", HelpText = "Print responses when running script")]
        public bool PrintScriptResponses { get; set; }
    }
}