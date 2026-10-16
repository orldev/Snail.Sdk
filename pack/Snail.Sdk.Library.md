# Snail.Sdk.Library

The MSBuild SDK of a Snail library: [Snail.Sdk](https://www.nuget.org/packages/Snail.Sdk), plus the public API kept
in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` so any change to it shows in the diff, validation against
the last published package, native compatibility unless the library says why not, symbol packages, and an API
reference generated from the assembly on request.

```xml
<Project Sdk="Snail.Sdk.Library">
```

```json
{ "msbuild-sdks": { "Snail.Sdk.Library": "0.1.0" } }
```

`dotnet new s3l-lib` from [Snail.Sdk.Templates](https://www.nuget.org/packages/Snail.Sdk.Templates) starts a library
on it.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)
