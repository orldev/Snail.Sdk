# Installing servers locally

How a Snail MCP server is installed on a developer's machine from its source, registered with Claude Code, kept running
by launchd when every session shares it, and checked as installed. A server run from `bin/Debug` is an unoptimised
build that the next `dotnet build` replaces under running sessions and `dotnet clean` removes; an installed server lives
apart from its source, is built for the job, and changes only when it is installed again. Releases to NuGet are
[releasing](releasing.md).

## Where it lives

```
~/.s3l-mcp/
  Snail.MCP.CleanUp/
    2026-10-12-1430.988248c/        one publication, never changed afterwards
    2026-10-14-0915.1a2b3c4-dirty/
    current -> 2026-10-14-0915.1a2b3c4-dirty
  Snail.MCP.Memory/
    2026-10-13-1100.5d6e7f8/
    current -> 2026-10-13-1100.5d6e7f8
    s3l.mcp.memory.plist            the launchd job of a shared server
```

- **One folder per server, named after its package** (`SnailMcpName`, the package id unless the project sets another):
  the name the repository and the package already carry.
- **Only programs live here.** A server keeps its data in `~/.s3l-mcp-<short>` and its settings in
  `~/.config/s3l-mcp-<short>/` (`ServerFolders` of `Snail.Sdk.Mcp.Hosting`), so `~/.s3l-mcp` can be deleted and
  installed again without losing anything.
- **Every publication is a folder of its own**, named `<date>-<time>.<commit>`, with `-dirty` when the working tree had
  uncommitted changes. Nothing is ever written over a program a session is running: macOS may end a process whose signed
  binary is replaced under it, and a dll replaced under a running server breaks it later. A second install of the same
  commit in the same minute is refused for the same reason.
- **`current` points at the publication in use.** An install switches it in one step. Sessions already running keep the
  publication they started with until they reconnect; going back is pointing `current` at the folder before.
- **The newest publications are kept** — three, or `SnailMcpKept` — and the one `current` names, and any a running process
  still uses (a file of it is open, or a command line names it). The rest are removed by the next install.

## Installing

```bash
dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true    # a native server
dotnet publish src -c Release -p:SnailMcpInstall=true                 # a framework-dependent server
```

There is no script to keep in step with the build: the build of `Snail.Sdk.Mcp` is the installer. In order, it

1. names the publication from the date, the time and `git rev-parse --short HEAD`, and publishes into its folder;
2. publishes the server's companions into the same folder — programs it ships with, listed in its project as
   `<SnailMcpCompanion Include="…csproj" />`;
3. starts a native program once with nothing on its input (and its companions likewise), with `SnailMcpWarmUp` as its
   arguments when the program needs some to return at once: macOS checks a new binary the first time it runs, and that
   happens now rather than when the first session starts it;
4. points `current` at the new publication;
5. removes the publications it does not keep;
6. for a server kept running by launchd, writes its job and restarts it or prints how to load it (below).

The install writes only under `~/.s3l-mcp` (`SnailMcpRoot`). Registrations, hooks and launchd jobs are the owner's to
change: the commands are printed, never run.

## Registering in Claude Code

Every session gets the installed server through `current`, at user scope, so installing again changes nothing in the
configuration:

```bash
# a native server: the program itself
claude mcp add -s user clean -- /Users/<you>/.s3l-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp

# a framework-dependent server: dotnet and the dll
claude mcp add -s user roslyn -- dotnet /Users/<you>/.s3l-mcp/Snail.MCP.Roslyn/current/Snail.MCP.Roslyn.dll

# a shared server over HTTP: its loopback address
claude mcp add -s user --transport http memory http://127.0.0.1:<port>/mcp
```

The path must be absolute: Claude Code does not expand `~` (it does expand `${HOME}`). `claude mcp add` refuses a name
that already exists at the same scope, so `claude mcp remove -s user <name>` comes first. Settings go in as
`-e SNAIL_MCP_<SHORT>_…=value`.

**Hooks go through `current` too.** A server that installs hooks into Claude Code writes their commands with the path
of the installed program, so installing again changes no hook: Snail.MCP.CleanUp writes them with
`…/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp install-hooks --forwarder …/Snail.MCP.CleanUp/current/s3l-mcp-cleanup-hook`,
its native forwarder being one of its companions. What a program writes about itself names a stable path
(`Invocation.Stable` of `Snail.Sdk.Mcp.Hosting`): the path a running program reports has every link resolved — the dated
publication behind `current`, or a dotnet tool's `.store/<id>/<version>/…` — and both are gone after the next install or
`dotnet tool update`.

## Shared servers under launchd

Over stdio every session starts a server of its own. A server every session shares runs once, over HTTP on a loopback
address (`SnailMcpTransport` `Http` or `Both`), and launchd keeps it running. Its repository keeps the job template,
`deploy/s3l.mcp.<short>.plist` (or the file `SnailMcpLaunchAgent` names): the file name is the job's label, and
`{{HOME}}`, `{{CURRENT}}` and `{{DOTNET}}` become this machine's home, the server's `current` folder and the dotnet
running the build. The template's job starts the server at login and again whenever it ends, with
`SNAIL_MCP_<SHORT>_TRANSPORT=http`, and logs to `~/Library/Logs/<Package>.log`.

The install writes the job to `~/.s3l-mcp/<Package>/s3l.mcp.<short>.plist` — never into `~/Library/LaunchAgents` — and
then asks launchd what it holds:

| launchd holds | The install |
| --- | --- |
| no `s3l.mcp.<short>` | prints `s3l.mcp.<short> is not loaded. To load it:` and the commands below |
| `s3l.mcp.<short>`, loaded from the job just written — the same bytes, or the same keys and values once `plutil` has rewritten both, so a reworded comment of the template changes nothing | restarts it, `launchctl kickstart -k gui/<uid>/s3l.mcp.<short>`, on the publication `current` now names, and prints `s3l.mcp.<short> restarted` |
| `s3l.mcp.<short>`, loaded from another plist or a changed one | restarts nothing — `kickstart -k` would not read the new plist — and prints that the job is loaded from another file, then `launchctl bootout gui/<uid>/s3l.mcp.<short>` and the commands below |

The commands that load the job, printed with this machine's uid:

```bash
cp "/Users/<you>/.s3l-mcp/<Package>/s3l.mcp.<short>.plist" "/Users/<you>/Library/LaunchAgents/"
launchctl bootstrap gui/<uid> "/Users/<you>/Library/LaunchAgents/s3l.mcp.<short>.plist"
```

When launchd also holds the same server under its snail name, `com.snail.mcp.<short>` — on the same port, so the new job
could not listen — the install prints first `launchctl bootout gui/<uid>/com.snail.mcp.<short>` and the `rm` of the
plist it was loaded from, so the next login does not start it again; with the new job already loaded beside it, the
restart waits for those commands.

**Keep the loaded job the one written.** A job edited after it was loaded is no longer restarted by an install, and the
commands printed instead would load the template's job over the edits. A setting of one machine — a token above all —
therefore goes where the server reads it itself: its settings file, `~/.config/s3l-mcp-<short>/config.json`, readable
by the owner alone (`chmod 600`), which every server reads through `ServerSettings` of `Snail.Sdk.Mcp.Hosting`. A
variable is a field of the file under the same name the variable gives it: `SNAIL_MCP_FIGMA_ACCESS_TOKEN` is
`"AccessToken"`, `SNAIL_MCP_FIGMA_CACHE__TTL_HOURS` is `"Cache": { "TtlHours": … }`. A secret in a job or a
registration travels wherever that file is copied; the settings file serves stdio sessions too, and a service manager
other than launchd needs nothing of its own. A setting every machine shares goes into the template, and conformance
refuses a secret there (S3LC0029).

A job loaded from a copy of the one written, with variables of the server added by hand and nothing else changed, is
not restarted either: the install names each variable as the field it is in the settings file — never its value — and
then prints the commands that load the new job, to run once they are moved.

## The idle trim

Every server built on `McpHost` or `McpHttpHost` gives back the memory its last piece of work left behind once it has
gone quiet. When no MCP message has come for five minutes, over stdio or any HTTP session, and the process has allocated
at least 32 MB since the last trim, it runs one compacting collection of every generation, at most once per quiet period.
A server that does nothing between calls allocates nothing, and is never paused; Snail.MCP.CleanUp, idle after a census,
went from 244 MB to 72 MB for a pause of about 50 ms.

`SNAIL_MCP_IDLE_TRIM`, read once when the server starts, changes it:

| Value | The trim |
| --- | --- |
| unset, or not a number | after five minutes of quiet |
| a number of seconds | after that many seconds of quiet |
| `0` | off |

It is one variable for every server, not a `SNAIL_MCP_<SHORT>_IDLE_TRIM` of each: the trim belongs to the host of
`Snail.Sdk.Mcp.Hosting`, as a server's own settings belong to the server and carry its prefix. Set for one server, it
goes into that server's registration (`claude mcp add … -e SNAIL_MCP_IDLE_TRIM=0`) or, for a shared server, the
`EnvironmentVariables` of its job template in `deploy/` — not of the loaded job, which an install would then stop
restarting; set in the shell, it applies to every server started from it. Each trim logs the bytes committed before
and after and the pause at debug level, which the stderr lines of `UseStderr`, from information up, leave out: it shows
in a logger that writes debug.

## Checking an installed server

The contract tests start the server through `Snail.Sdk.Mcp.Testing`: the dll beside the tests by default, or the program
a variable names. Pointed at the installation, they check the installed program itself, with a home folder of their own:

```bash
SNAIL_CLEANUP_SERVER=/Users/<you>/.s3l-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp \
  dotnet test tests --filter "FullyQualifiedName~Contract"
```

A native build fails at a call, not at the build, when it was not given a type it needs; this is where that shows before a
session meets it.
