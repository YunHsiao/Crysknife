cd `dirname $0`
dotnet build > /dev/null
dotnet run -- "$@"
