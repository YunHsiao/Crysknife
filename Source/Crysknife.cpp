// SPDX-FileCopyrightText: Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

#include "CoreMinimal.h"
#include "Modules/ModuleInterface.h"

class FCrysknifeModule final : public IModuleInterface {};
IMPLEMENT_MODULE(FCrysknifeModule, Crysknife);
