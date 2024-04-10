# SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
# SPDX-License-Identifier: MIT

cd `dirname $0`/Source

for Arg in "$@"; do
    if [[ $Arg = "--skip-build" ]]; then
        Skip=true
    fi
done

[ -z "$Skip" ] && dotnet build -c Release > /dev/null
./bin/Release/net6.0/Crysknife "$@"
