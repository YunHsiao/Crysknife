# SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
# SPDX-License-Identifier: MIT

ROOTDIR=`cd "$(dirname "$0")"; pwd`
cd $ROOTDIR/Crysknife

for (( i=1; i<=$#; i++)); do
  case ${!i} in
    --skip-build) Skip=true;;
    --loc) Loc=true;;
    -P)
      j=$((i+1))
      Project=${!j}
      ;;
  esac
done

[ -z "$Skip" ] && dotnet build -c Release > /dev/null
./bin/Release/net6.0/Crysknife "$@"

if [ ! -z "$Loc" ]; then
    cd $ROOTDIR/../$Project
    SOURCE=`find . -path './Source/*.*' -not -path "./Source/ThirdParty/*" | xargs wc -l`
    if [ "$(uname)" == "Darwin" ]; then
        NEW=`find -E . -regex './SourcePatch/.*\.(cpp|h)' | xargs wc -l`
    else
        NEW=`find . -regex './SourcePatch/.*\.\(cpp\|h\)' | xargs wc -l`
    fi
    P=`find . -path './SourcePatch/*.patch' | xargs grep ^+ | grep -o %0a | wc -l`

    S=`awk '/total/{k+=$1}END{print k}' <<< "$SOURCE"`
    N=`awk '/total/{k+=$1}END{print k}' <<< "$NEW"`

    printf '%20s : %s\n' 'Plugin Source LOC' $S
    printf '%20s : %s\n' 'Engine New File LOC' $N
    printf '%20s : %s\n' 'Engine Patched LOC' $P
fi
