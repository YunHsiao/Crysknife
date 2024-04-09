# SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
# SPDX-License-Identifier: MIT

cd `dirname $0`/Source
dotnet build > /dev/null
dotnet run -- "$@"
