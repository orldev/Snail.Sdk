#!/bin/sh
# The launchd job of a server just installed: restarted when the job launchd holds is the one just written, otherwise
# the commands that load it, printed for the owner to run. Nothing here loads, unloads or replaces a job, except the
# restart of the very job just written.
#
#   sh Snail.Sdk.Mcp.LaunchAgent.sh <launchctl> <label> <job just written> <label of the same server under the snail name>
#
# A server published before the s3l names ran as com.snail.mcp.<short>: on the same port as the new job, so it has to
# go before the new one is loaded, and its plist with it, or the next login starts both again.

launchctl=$1
label=$2
job=$3
old=$4

domain="gui/$(id -u)"
agents="$HOME/Library/LaunchAgents"

# The plist launchd loaded a job from: launchctl print names it on its path line.
loaded_from() {
    "$launchctl" print "$domain/$1" 2> /dev/null | sed -n 's/^[[:space:]]*path = //p' | head -n 1
}

load() {
    echo "  cp \"$job\" \"$agents/\""
    echo "  launchctl bootstrap $domain \"$agents/$label.plist\""
}

# The job under the snail name, when launchd still holds it: what retires it, printed first.
retire() {
    if [ -n "$old" ] && "$launchctl" print "$domain/$old" > /dev/null 2>&1; then
        previous=$(loaded_from "$old")
        echo "  launchctl bootout $domain/$old"

        if [ -n "$previous" ]; then
            echo "  rm \"$previous\""
        fi
    fi
}

retired=$(retire)

if ! "$launchctl" print "$domain/$label" > /dev/null 2>&1; then
    echo "$label is not loaded. To load it:"
    [ -n "$retired" ] && echo "$retired"
    load
    exit 0
fi

current=$(loaded_from "$label")

# Whether launchd would read the same job from both plists: the same bytes, or the same keys and values once plutil has
# rewritten both, which drops comments and layout. A template whose comment was reworded is still the job loaded.
same_job() {
    cmp -s "$1" "$2" && return 0
    command -v plutil > /dev/null 2>&1 || return 1
    scratch=$(mktemp -d) || return 1
    plutil -convert xml1 -o "$scratch/loaded.plist" -- "$1" > /dev/null 2>&1 &&
        plutil -convert xml1 -o "$scratch/written.plist" -- "$2" > /dev/null 2>&1 &&
        cmp -s "$scratch/loaded.plist" "$scratch/written.plist"
    same=$?
    rm -rf "$scratch"
    return $same
}

if [ -n "$current" ] && same_job "$current" "$job"; then
    if [ -z "$retired" ]; then
        "$launchctl" kickstart -k "$domain/$label" || exit 1
        echo "$label restarted"
        exit 0
    fi

    echo "$label is loaded, and so is $old, the same server under its snail name. To keep only $label:"
    echo "$retired"
    echo "  launchctl kickstart -k $domain/$label"
    exit 0
fi

# The variables a job edited by hand carries beyond the job just written, when they are all the difference: the two
# jobs alike once their EnvironmentVariables are taken out. Empty when anything else differs, or plutil is not there.
handwritten() {
    command -v plutil > /dev/null 2>&1 || return 0
    scratch=$(mktemp -d) || return 0
    cp "$1" "$scratch/loaded.plist" && cp "$2" "$scratch/written.plist" || { rm -rf "$scratch"; return 0; }

    for side in loaded written; do
        plutil -remove EnvironmentVariables "$scratch/$side.plist" > /dev/null 2>&1
        plutil -convert xml1 "$scratch/$side.plist" > /dev/null 2>&1 || { rm -rf "$scratch"; return 0; }
    done

    if cmp -s "$scratch/loaded.plist" "$scratch/written.plist"; then
        plutil -extract EnvironmentVariables xml1 -o - -- "$1" 2> /dev/null | sed -n 's/.*<key>\(.*\)<\/key>.*/\1/p' |
            while read -r variable; do
                mine=$(plutil -extract "EnvironmentVariables.$variable" raw -o - -- "$1" 2> /dev/null)
                theirs=$(plutil -extract "EnvironmentVariables.$variable" raw -o - -- "$2" 2> /dev/null) || theirs=
                [ "$mine" = "$theirs" ] || echo "$variable"
            done
    fi

    rm -rf "$scratch"
}

# SNAIL_MCP_FIGMA_ACCESS_TOKEN as the field of the settings file it is: AccessToken; a section, Section:Field.
field() {
    echo "${1#"$prefix"}" | awk -F'__' '{
        for (s = 1; s <= NF; s++) {
            n = split($s, words, "_"); out = ""
            for (w = 1; w <= n; w++) out = out toupper(substr(words[w], 1, 1)) tolower(substr(words[w], 2))
            printf "%s%s", (s > 1 ? ":" : ""), out
        }
        print ""
    }'
}

short=${label#s3l.mcp.}
prefix="SNAIL_MCP_$(echo "$short" | tr '[:lower:]' '[:upper:]')_"
moved=$( [ -n "$current" ] && handwritten "$current" "$job" )

if [ -n "$moved" ] && ! echo "$moved" | grep -qv "^$prefix"; then
    echo "$label is loaded from $current, which differs from the job just written only in its EnvironmentVariables;"
    echo "loading the new job would drop them, so nothing is restarted. Move them into the settings file the server reads"
    echo "itself, ~/.config/s3l-mcp-$short/config.json, readable by you alone (chmod 600):"
    echo "$moved" | while read -r variable; do echo "  $variable -> \"$(field "$variable")\""; done
    echo "then load the new job:"
    [ -n "$retired" ] && echo "$retired"
    echo "  launchctl bootout $domain/$label"
    load
    exit 0
fi

echo "$label is loaded from ${current:-a plist launchctl does not name}, which is not the job just written, $job;"
echo "a restart would keep the old job. To load the new one:"
[ -n "$retired" ] && echo "$retired"
echo "  launchctl bootout $domain/$label"
load
