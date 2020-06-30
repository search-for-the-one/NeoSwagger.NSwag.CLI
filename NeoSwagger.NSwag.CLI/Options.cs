﻿using CommandLine;

namespace NeoSwagger.NSwag.CLI
{
    public class Options
    {
        [Value(0, Required = true, MetaName = "swaggerEndpoint", HelpText = "Specify the Swagger Document endpoint.")]
        public string SwaggerEndpoint { get; set; }

        [Option('s', "script", HelpText = "Specify a script file.")]
        public string ScriptFile { get; set; }

        [Option('q', "quit", HelpText = "Don't run interactive shell (run script and quit).")]
        public bool RunInteractiveShell { get; set; }

        [Option('p', "print", HelpText = "Print responses when running script.")]
        public bool PrintScriptResponses { get; set; }

        [Option('l', "printlength", Default = 1200, HelpText = "Set maximum response text length to print (-1 for infinite).")]
        public int PrintMaxTextLength { get; set; }
    }
}