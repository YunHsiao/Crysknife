# SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
# SPDX-License-Identifier: MIT

if [ $# -eq 0 ]; then
    set -- "12"
fi

STEP=$1

if [[ $STEP == *"1"* ]]; then
    sphinx-build -b gettext . _build/gettext
    sphinx-intl update -p _build/gettext -l zh_CN
fi

if [[ $STEP == *"2"* ]]; then
    sphinx-build -b html . _build/html/en
    sphinx-build -b html -D language=zh_CN . _build/html/zh_CN
fi
