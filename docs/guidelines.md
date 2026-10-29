# Coding guidelines

How C# is written in every Snail library and server. These are rules, not counts: they decide, and the code only shows
them being followed. Where the build checks a rule, the rule names the check — an analyzer of Snail.Sdk
([the rules](rules/index.md)) or a rule of the .NET SDK that `Snail.Sdk.globalconfig` makes a warning, an error under
warnings as errors. The rest no analyzer can judge, and is held by review. How the checks are layered:
[the standard's Code style](standard.md#code-style).

## Names

### No smurf naming, one ubiquitous language

- **No redundant affixes.** No prefix or suffix that only restates the type or the namespace, and never the class name
  repeated in its own members: `Users.Add(user)`, not `UserManager.ManageUser(user)`; `Card.Of`, not `Card.CreateCard`.
- **No pattern names.** A name says what the thing is, never which pattern it implements: no `Service`, `Manager`,
  `Helper`, `Processor`, `Data` or `Util` suffix bolted on to make a name look like a type — `SolutionOpener`,
  `PatternSearch`, `TagRules`. ([S3L0006](rules/S3L0006.md))
- **The language of the domain.** Classes and methods are named after the processes they serve, not after their
  technical implementation.

### Naming semantics

- **No data types in names**: `users`, never `userList` or `userDict`. ([S3L0007](rules/S3L0007.md))
- **Booleans ask**: booleans and methods returning one read as yes/no questions — `isActive`, `hasPermission`,
  `CanExecute()` — so that `if (hasPermission)` reads as plain English. ([S3L0008](rules/S3L0008.md))
- **Positive phrasing**: no negatives to negate — `isAvailable`, never `isNotUnavailable`.
- **No abbreviations** that are not already words of the domain. A local is named for what it holds, not for its type
  or its position in a loop: `scored`, `matched`, `wanted`.
- **Casing**: PascalCase types, members, constants and static readonly fields; `_camelCase` private fields; camelCase
  locals and parameters; `I` before an interface; `Async` after an async method. (IDE1006)

A name already shipped in `PublicAPI.Shipped.txt` stays as it is: renaming a released member breaks its users, which is
a decision for a major version, so the naming rules ask nothing of it.

## Layout and breathing

- **One blank line between members**, never two.
- **A blank line between the phases of a method** — validation, then execution; arrange, act, assert; in a service
  method: validation, preparing the request, executing it, mapping the answer.
- **A blank line before the final `return`** of a block of four statements or more: the answer is a phase of its own.
  ([S3L0003](rules/S3L0003.md))
- Since comments inside members are forbidden, this spacing is what chunks the logic and makes it readable at a glance.
- **File-scoped namespaces** matching the folders; usings outside them, none unused. (IDE0161, IDE0130, IDE0065, IDE0005)

## Idiomatic, minimal C#

- **Pragmatic, modern C#** of the language version the SDK band brings. No AI-isms: no redundant null checks, no
  `this.` ([S3L0009](rules/S3L0009.md)), no defensive code where the context already guarantees safety.
- **Collection expressions**: `[]` and `[.. items]`, never `new List<int>()` or `new int[]`. (IDE0028, IDE0300–IDE0305)
- **Primary constructors** where a type takes its dependencies. (IDE0290)
- **Expression-bodied members** for simple getters, properties and one-line methods.
- **`var` only where the type is obvious** from the right-hand side (`var user = new User()`); otherwise the type is
  stated: `int count = CalculateTotal()`.
- **Keywords for types**: `int`, `string`, `bool`, never `Int32`, `String`, `Boolean`. ([S3L0010](rules/S3L0010.md))
- **Accessibility always written**; fields `readonly` where they can be. (IDE0040, IDE0044)

## Flat structure

- **Guard clauses and early returns**: validate and return — or throw — as early as possible. No `else` after a
  `return`.
- **Grow down, not right**: code reads top to bottom rather than drifting rightward; two to three levels of nesting at
  most.
- **Unpack the query**: a LINQ chain of more than four operators, or a lambda nested two deep, becomes named steps —
  intermediate variables that say what each stage holds. ([S3L0001](rules/S3L0001.md))
- **Create, then return**: an object built across more than fifteen lines is assigned to a named variable first, and the
  variable is returned or yielded. A short object built in place stays in the `return`. ([S3L0002](rules/S3L0002.md))

## Strings

- **Interpolation**: `$"{value}"`; no concatenation of values with `+`, no `string.Format`.
- **Raw string literals** for anything multi-line.
- **Every comparison states its `StringComparison`**: a culture-dependent comparison is a bug that shows only on another
  machine. (CA1307, CA1309, CA1310)

## Comments and documentation

- **No comments inside member bodies.** The logic explains itself through names and structure; a block that cannot be
  understood without a comment becomes a well-named private method. No banner comments, no `#region`, no
  commented-out code, no `TODO` left behind. ([S3L0004](rules/S3L0004.md))
- **Every public member carries an XML documentation comment**, and CS1591 is never suppressed in shipped code.
- **`<summary>` is one sentence** stating what the member answers, not a restatement of its name. No `<param>` or
  `<returns>` that merely repeats the parameter's name.
- **`<remarks>` carries the why**: the constraint, the measurement or the failure that made the code what it is — the part
  no reader can reconstruct, and the only place length is earned. A measured constant is written together with the
  measurement that produced it.
- **English only**: names, documentation, comments, log messages and the domain's terms are professional technical
  English. A string an LLM must read verbatim may be in another language, and lives in a type or namespace named for
  prompts. ([S3L0005](rules/S3L0005.md)) Text a person reads in their own language lives in resources — `Messages.resx`
  in English and a `Messages.<language>.resx` per other language — and is read through one `Messages` class, never
  written in code. ([S3LC0031](conformance.md))

## Architecture and registration

### Composition root

- **A clean `Program.cs`**: the entry point says which features are on, never how. No `AddSingleton`, `AddTransient` or
  the like in it.
- **Each feature registers itself** through an `IServiceCollection` (or `IMcpServerBuilder`) extension of its own.
- **Matryoshka**: a root extension calls smaller, specific ones (`ConfigureFeature()` → `AddRepositories()` →
  `AddDomainServices()`); upper layers know nothing of the internals of lower ones.
- **C# 14 extension blocks** where the target framework allows; otherwise static classes with
  `this IServiceCollection services`.

### Layers

- `Domain` holds pure records and knows nothing else. `Application/Ports` holds the interfaces. `Adapters` implement them
  and are the only place a framework, a database, the file system or MSBuild is touched. `Extensions` compose. `Tools`
  only delegate — a tool method is one expression calling a port.
- **Folders follow features, not patterns.** There is no `Helpers`, `Utils`, `Managers`, `Common` or `Shared`.
  ([S3L0006](rules/S3L0006.md))

## Types, failure and asynchrony

- **Types**: `sealed record` by default; `readonly record struct` for a small value carried by value. Positional
  parameters while they stay few; `init` properties once there are enough to misorder. Collections are exposed as
  `IReadOnlyList<T>`. Nullable is enabled and honoured: a nullable return means "unknown", and the caller is made to
  answer it.
- **Failure**: exceptions are for the impossible, not for control flow; what a caller can act on comes back as data. An
  empty answer explains itself — "nothing is indexed" and "nothing matched" are different facts, and a result carries
  the note that says which, rather than a bare empty list.
- **Asynchrony**: `async` methods end in `Async`, take a `CancellationToken` and pass it to everything they call. No
  `async void`, no `.Result`, no `.Wait()`. (VSTHRD002, VSTHRD100, VSTHRD101, VSTHRD103, VSTHRD110)
- **Disposal**: a type disposes the fields it owns. (CA2213)
- **Performance with a measured gain**: cached `JsonSerializerOptions` (CA1869), `SearchValues` (CA1870), one
  enumeration (CA1851), `LoggerMessage` for logging (CA1848), sealed internal types (CA1852), `GeneratedRegex`
  (SYSLIB1045).

## MCP servers and agents

To a model, a tool's description is not a string but a contract that changes its behaviour. Descriptions are managed as
strictly as code.

- **Two stores, split by audience**: `Messages` — what users read, free to change; `ToolDescriptions` — instructions,
  limits and schemas a model reads, changed with care, since they steer the agent.
- **Compile-time constants**: attributes take `const string`, so a tool's description is static.
- **Every type a tool takes or gives** beyond the primitives goes into a source-generated `JsonSerializerContext` of the
  server, passed through `ToolJson.Options` of `Snail.Sdk.Mcp.Hosting`: a native build cannot look a type up at run time.
- **A long operation is a job, never a poll.** Work that outlasts a call — a render, an index pass, a test run — runs
  through `Jobs.Start` of `Snail.Sdk.Mcp.Hosting`: the tool takes `int wait = 60`, answers within it with the result
  (`job.ForAsync`) or with the job's `job://` resource, and the client that can wait is told of the end by its
  subscription (`Snail.Sdk.Mcp.JobClient`). No `*_status` tool — the resource is the status — and no description that
  says "poll" or "call again": a model made a timer spends a turn on every look. Progress within the call stays
  `IProgress<ProgressNotificationValue>`. (S3LC0032; `ToolGuards` names a description that tells the model to poll)
- **Settings** come from the server's settings file and then its own prefixed variables, `SNAIL_MCP_<SHORT>_…`, which
  win — `ServerSettings.Read(name)` before a host exists, the same sources in every host `McpHost` and `McpHttpHost`
  build; no server keeps a lookup chain of its own. A secret goes into the file, never into a job or a registration.
  Its folders come from `ServerFolders`.

### Guard tests

Descriptions written by hand drift from the code they describe, so reflection-based guard tests keep them in step:

- **Enum completeness**: every value of a relevant enum is named in the description that offers it; adding a value fails
  the test until the description names it.
- **Limits**: a limit stated in prose equals the limit in code — "at most 40 KB" fails unless the limit is `40960` bytes.
- **Coverage**: every registered tool has a description.

`ToolGuards.Problems` of `Snail.Sdk.Mcp.Testing` checks the list a client receives: snake-case names listed once, the
tool and every parameter described, read-only or destructive stated, every allowed value named as a word of its own,
English, and no description that tells the model to poll.

## Tests

- xUnit, named `Subject_Condition_ExpectedResult`.
- A test asserts one behaviour and reads as a sentence about the system, not about the code under it.
- A test whose reason is not obvious carries the same `<summary>` a member would.
