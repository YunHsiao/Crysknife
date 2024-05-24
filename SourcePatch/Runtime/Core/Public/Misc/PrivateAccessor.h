// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

/**
 * A tiny library for accessing private members from non-friend classes.
 *
 * Under the hood this is done by exploiting the fact that
 * accessibilities are disregarded during explicit instantiation.
 *
 * Inspired by: http://bloglitb.blogspot.com/2010/07/access-to-private-members-thats-easy.html
 */

#pragma once

template<typename Type, Type& Output, Type Input>
struct TRob
{
	TRob() { Output = Input; }
	static TRob Obj;
};

template<typename Type, Type& Output, Type Input>
TRob<Type, Output, Input> TRob<Type, Output, Input>::Obj;

#define INIT_PRIVATE_ACCESSOR(Name, Value) \
	template struct TRob<decltype(Name), Name, &Value> // <-- Where the magic happens

#define DEFINE_PRIVATE_ACCESSOR(Name, Value, Type, ...) \
	static Type<__VA_ARGS__> Name; \
	INIT_PRIVATE_ACCESSOR(Name, Value)

/****************************** Syntactic Sugars ******************************/

template<typename OwnerType, typename VariableType>
using TMemberVariableType = VariableType(OwnerType::*);
#define DEFINE_PRIVATE_ACCESSOR_VARIABLE(Name, Class, VariableType, VariableName) \
	DEFINE_PRIVATE_ACCESSOR(Name, Class::VariableName, TMemberVariableType, Class, VariableType)

template<typename OwnerType, typename ReturnType, typename... Args>
using TMemberFunctionType = ReturnType(OwnerType::*)(Args...);
#define DEFINE_PRIVATE_ACCESSOR_FUNCTION(Name, Class, ReturnType, FunctionName, ...) \
	DEFINE_PRIVATE_ACCESSOR(Name, Class::FunctionName, TMemberFunctionType, Class, ReturnType, __VA_ARGS__)

template<typename OwnerType, typename ReturnType, typename... Args>
using TConstMemberFunctionType = ReturnType(OwnerType::*)(Args...) const;
#define DEFINE_PRIVATE_ACCESSOR_CONST_FUNCTION(Name, Class, ReturnType, FunctionName, ...) \
	DEFINE_PRIVATE_ACCESSOR(Name, Class::FunctionName, TConstMemberFunctionType, Class, ReturnType, __VA_ARGS__)

template<typename VariableType>
using TStaticVariableType = VariableType*;
#define DEFINE_PRIVATE_ACCESSOR_STATIC_VARIABLE(Name, Class, VariableType, VariableName) \
	DEFINE_PRIVATE_ACCESSOR(Name, Class::VariableName, TStaticVariableType, VariableType)

template<typename ReturnType, typename... Args>
using TStaticFunctionType = ReturnType(*)(Args...);
#define DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION(Name, Class, ReturnType, FunctionName, ...) \
	DEFINE_PRIVATE_ACCESSOR(Name, Class::FunctionName, TStaticFunctionType, ReturnType, __VA_ARGS__)

#define PRIVATE_ACCESS_OBJ(Obj, Name) (Obj.*Name)
#define PRIVATE_ACCESS_PTR(Ptr, Name) (Ptr->*Name)
#define PRIVATE_ACCESS_STATIC(Name) (*Name)

/****************************** Use Cases ******************************/

#if 0

// For any class with private members:

#include <cstdint>
#include <cstdio>
#include <map>

class FTestClass
{
	static const FTestClass* Instance;
	static bool Register(const FTestClass* Ptr) { Instance = Ptr; return true; }

	int32_t Value = 42;
	void Increment() { Value++; }
	void Register(std::map<const FTestClass*, int32_t>& Dictionary) const { Dictionary[this] = Value; }

public:
	void Print() const { printf("Instance %p Value %d\n", Instance, Value); }
};

inline const FTestClass* FTestClass::Instance = nullptr;

// Define accessors as follows:

DEFINE_PRIVATE_ACCESSOR_STATIC_VARIABLE(TestClassInstance, FTestClass, const FTestClass*, Instance);
DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION(TestClassRegister, FTestClass, bool, Register, const FTestClass*);

DEFINE_PRIVATE_ACCESSOR_VARIABLE(TestClassValue, FTestClass, int32_t, Value);
DEFINE_PRIVATE_ACCESSOR_FUNCTION(TestClassIncrement, FTestClass, void, Increment);

// Alias complex type names so we can pass them to macros
using FTestClassIndexMap = std::map<const FTestClass*, int32_t>;
// Overloaded functions also works
DEFINE_PRIVATE_ACCESSOR_CONST_FUNCTION(TestClassRegister2, FTestClass, void, Register, FTestClassIndexMap&);

// Use it anywhere!

inline void PrivateAccessorTest()
{
	// Where our target data is stored
	FTestClass Obj;
	const FTestClass* Ptr = &Obj;

	// Get member variable
	const int32_t* Value = &PRIVATE_ACCESS_PTR(Ptr, TestClassValue);

	// Invoke member function
	PRIVATE_ACCESS_OBJ(Obj, TestClassIncrement)();

	// Invoke static function
	bool bSuccess = PRIVATE_ACCESS_STATIC(TestClassRegister)(Ptr);

	// Set static variable
	PRIVATE_ACCESS_STATIC(TestClassInstance) = reinterpret_cast<const FTestClass*>(static_cast<intptr_t>(0xdeadbeef));

	// Invoke overloaded function
	FTestClassIndexMap TestClassIndexMap;
	PRIVATE_ACCESS_PTR(Ptr, TestClassRegister2)(TestClassIndexMap);

	Obj.Print();
	printf("LocalValue %d Success %d MapValue %d\n", *Value, bSuccess, TestClassIndexMap[Ptr]);
	fflush(stdout);
}

#endif
