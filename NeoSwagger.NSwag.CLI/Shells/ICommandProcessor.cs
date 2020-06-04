using System.Threading.Tasks;

namespace NeoSwagger.NSwag.CLI.Shells
{
    internal interface ICommandProcessor
    {
        Task<Response> Execute(string line);
        string GetHelp(string service = "");
    }
}