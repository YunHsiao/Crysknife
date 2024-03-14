cd `dirname $0`/Source
dotnet build > /dev/null
dotnet run -- "$@"
