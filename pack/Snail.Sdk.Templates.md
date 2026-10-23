# Snail.Sdk.Templates

The templates every new Snail project starts from, on [Snail.Sdk](https://www.nuget.org/packages/Snail.Sdk) of the same
release:

```bash
dotnet new install Snail.Sdk.Templates
dotnet new s3l-lib -n Snail.Toolkit.Demo     # a library on Snail.Sdk.Library
dotnet new s3l-mcp -n Snail.MCP.Demo         # an MCP server on Snail.Sdk.Mcp
dotnet new s3l-web -n Vertical.Demo          # an ASP.NET application on Snail.Sdk
```

Each comes with its tests, the shared `.editorconfig` and `global.json`, and the workflows that call the shared ones.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)
