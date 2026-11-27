#!/usr/bin/env bash
set -euo pipefail

app_dir=$(cd -- "$(dirname -- "$0")" && pwd -P)
executable="$app_dir/HitAnalyzer2"
icon="$app_dir/hitanalyzer2.png"
template="$app_dir/hitanalyzer2.desktop"

for file in "$executable" "$icon" "$template"; do
    if [[ ! -f "$file" ]]; then
        printf 'Required file is missing: %s\n' "$file" >&2
        exit 1
    fi
done

if [[ "$app_dir" == *[[:cntrl:]]* || "$executable" == *=* ]]; then
    printf 'Move Hit Analyzer to a path without control characters or "=".\n' >&2
    exit 1
fi

# Exec uses command-line quoting as well as desktop-entry string escaping.
exec_path=${executable//\\/\\\\}
exec_path=${exec_path//\"/\\\"}
exec_path=${exec_path//\$/\\\$}
exec_path=${exec_path//\`/\\\`}
exec_path=${exec_path//%/%%}
exec_path=${exec_path//\\/\\\\}
icon_path=${icon//\\/\\\\}

data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
if [[ "$data_dir" != /* ]]; then
    data_dir="$HOME/.local/share"
fi
applications_dir="$data_dir/applications"
mkdir -p -- "$applications_dir"
chmod u+x -- "$executable"

desktop_file="$applications_dir/hitanalyzer2.desktop"
temporary_file=$(mktemp -- "$applications_dir/.hitanalyzer2.XXXXXX")
trap 'rm -f -- "$temporary_file"' EXIT

while IFS= read -r line || [[ -n "$line" ]]; do
    case "${line%$'\r'}" in
        Exec=*) printf 'Exec="%s"\n' "$exec_path" ;;
        Icon=*) printf 'Icon=%s\n' "$icon_path" ;;
        *) printf '%s\n' "${line%$'\r'}" ;;
    esac
done < "$template" > "$temporary_file"

mv -fT -- "$temporary_file" "$desktop_file"
printf 'Installed launcher: %s\n' "$desktop_file"
