using Snail.MCP.Template.Extensions;
using Snail.Sdk.Mcp.Hosting;

try
{
    await ServerHost.RunAsync(args);

    return 0;
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}
