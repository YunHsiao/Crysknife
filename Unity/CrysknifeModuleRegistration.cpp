#include "UnityPrefix.h"

#define UNITY_MODULE_HAS_INITIALIZE_CLEANUP 1

#include "Runtime/Modules/ModuleRegistration.h"

#define UNITY_MODULE_NAME Crysknife
#include "Runtime/Modules/ModuleTemplate.inc.h"
#undef UNITY_MODULE_NAME

UNITY_MODULE_INITIALIZE(Crysknife) {}
UNITY_MODULE_CLEANUP(Crysknife) {}
