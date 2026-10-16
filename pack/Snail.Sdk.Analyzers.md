# Snail.Sdk.Analyzers

The owner's code rules as Roslyn analyzers, with code fixes where a fix is mechanical: long LINQ chains unpacked into
named steps, a large object created before it is returned, a blank line before the answer, no comments inside members,
English only, no pattern names, no types in names, booleans that ask, no `this.` where nothing is shadowed, keyword
types. The rule ids start with `S3L`.

[Snail.Sdk](https://www.nuget.org/packages/Snail.Sdk) references this package and sets the severities; a project on
it needs no reference of its own.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The rules: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)
