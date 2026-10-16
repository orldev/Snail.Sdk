# Analyzer rules

`Snail.Sdk.Analyzers` carries the rules of the [guidelines](../guidelines.md) that no .NET analyzer has, and two the
.NET SDK has but a command-line build does not run. Every Snail project gets them through the SDK, so the rules hold in
CI and not only in an editor. This page lists each rule and suppressor; each has a page of its own, which is also where
the rule's link in an editor and in the build's warning line leads.

## Rules

Every rule is in the category `Snail` and is a warning, which `TreatWarningsAsErrors` makes an error in every Snail
project; a project that references the analyzers without the SDK is held to them as warnings. Generated code is not
analysed. The thresholds sit where the code of the fleet is, so a rule flags the outliers, not the habit.

| Id | Rule | Reports | Code fix |
| --- | --- | --- | --- |
| [S3L0001](S3L0001.md) | Unpack the query | a LINQ chain of more than 4 operators on a sequence, or a lambda nested 2 deep | — |
| [S3L0002](S3L0002.md) | Create, then return | an object built across more than 15 lines inside `return` or `yield return` | names the object, then returns it |
| [S3L0003](S3L0003.md) | Let it breathe | no blank line before the final `return` of a block of 4 statements or more | adds the blank line |
| [S3L0004](S3L0004.md) | No inline comments | a comment inside a member body | — |
| [S3L0005](S3L0005.md) | English only | Cyrillic in names, comments, and the text of code that is not a test, benchmark or prompt | — |
| [S3L0006](S3L0006.md) | No pattern names | a type ending in `Service`, `Manager`, `Helper`, `Processor`, `Data`, `Util`…; a `Helpers`, `Utils`, `Common`… namespace | — |
| [S3L0007](S3L0007.md) | No types in names | `userList`, `userDict`, `nameString` | — |
| [S3L0008](S3L0008.md) | Booleans ask | a boolean property or method that does not read as a question | — |
| [S3L0009](S3L0009.md) | No this qualifier | `this.` where nothing is shadowed (IDE0003) | removes it |
| [S3L0010](S3L0010.md) | Keyword types | `Int32`, `String` where the keyword exists (IDE0049) | writes the keyword |

## Suppressors

A suppressor silences another analyzer's finding where it cannot be acted on, and only there.

| Id | Name | Suppresses | Where |
| --- | --- | --- | --- |
| [S3LS0001](S3LS0001.md) | Shipped name | IDE1006 | on a type or member released in `PublicAPI.Shipped.txt` |
| [S3LS0002](S3LS0002.md) | Razor output | RS0041 | in the code Razor generates |

## Severities and suppression

The severities live in `Snail.Sdk.globalconfig`, shipped in the Snail.Sdk package, so a new release of the SDK brings
them to every repository at once and no repository keeps a copy. A repository does not lower a rule for all its code:
the shared `.editorconfig` is compared line for line by the [conformance check](../conformance.md) (S3LC0004), and a
`NoWarn` is reported as migration debt (S3LC0017). One place that must differ is suppressed there, with its reason:

```csharp
[SuppressMessage("Snail", "S3L0005:English only", Justification = "The sample text the parser is tested against.")]
```

A new rule, or a rule made stricter, comes in a new major version of Snail.Sdk: a build that passed must not fail
because a minor release came out.
