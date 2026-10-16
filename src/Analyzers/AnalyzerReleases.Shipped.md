; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
S3L0001 | Snail | Warning | QueryShape, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0001/)
S3L0002 | Snail | Warning | ReturnShape, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0002/)
S3L0003 | Snail | Warning | ReturnShape, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0003/)
S3L0004 | Snail | Warning | SourceText, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0004/)
S3L0005 | Snail | Warning | SourceText, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0005/)
S3L0006 | Snail | Warning | Naming, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0006/)
S3L0007 | Snail | Warning | Naming, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0007/)
S3L0008 | Snail | Warning | Naming, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0008/)
S3L0009 | Snail | Warning | Simplification, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0009/)
S3L0010 | Snail | Warning | Simplification, [Documentation](https://orldev.github.io/Snail.Sdk/rules/S3L0010/)

; Suppressors of release 1.0. Release tracking covers diagnostic descriptors only, so they are kept here as comments:
; S3LS0001 suppresses IDE1006 on a type or member shipped in PublicAPI.Shipped.txt (Released), https://orldev.github.io/Snail.Sdk/rules/S3LS0001/
; S3LS0002 suppresses RS0041 in the code Razor generates (Released), https://orldev.github.io/Snail.Sdk/rules/S3LS0002/
