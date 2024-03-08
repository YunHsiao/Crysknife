/**
 * A tiny library for accessing private members from non-friend classes.
 *
 * Under the hood this is done by exploiting the fact that
 * accessibilities are disregarded during explicit instantiation.
 *
 * Ref: http://bloglitb.blogspot.com/2010/07/access-to-private-members-thats-easy.html
 */

#pragma once

template<typename Tag>
struct TResult
{
	static typename Tag::Type Ptr;
};

template<typename Tag>
typename Tag::Type TResult<Tag>::Ptr;

template<typename Tag, typename Tag::Type Ptr>
struct TRob
{
	TRob() { TResult<Tag>::Ptr = Ptr; }
	static TRob Obj;
};

template<typename Tag, typename Tag::Type Ptr>
TRob<Tag, Ptr> TRob<Tag, Ptr>::Obj;

#define DEFINE_PRIVATE_ACCESSOR_VARIABLE_EX(ClassTag, Class, MemberType, MemberName) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef MemberType (Class::*Type); }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define DEFINE_PRIVATE_ACCESSOR_STATIC_VARIABLE_EX(ClassTag, Class, MemberType, MemberName) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef MemberType* Type; }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(ClassTag, Class, ReturnType, MemberName, Qualifier, ...) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef ReturnType(Class::*Type)(__VA_ARGS__) Qualifier; }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION_EX(ClassTag, Class, ReturnType, MemberName, ...) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef ReturnType(*Type)(__VA_ARGS__); }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define PRIVATE_ACCESS(ClassTag, MemberName) TResult<F##ClassTag##MemberName##PrivateAccessor>::Ptr

/****************************** Syntactic Sugars ******************************/

#define DEFINE_PRIVATE_ACCESSOR_VARIABLE(Class, MemberType, MemberName) DEFINE_PRIVATE_ACCESSOR_VARIABLE_EX(Class, Class, MemberType, MemberName)
#define DEFINE_PRIVATE_ACCESSOR_STATIC_VARIABLE(Class, MemberType, MemberName) DEFINE_PRIVATE_ACCESSOR_STATIC_VARIABLE_EX(Class, Class, MemberType, MemberName)
#define DEFINE_PRIVATE_ACCESSOR_FUNCTION(Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(Class, Class, ReturnType, MemberName, , __VA_ARGS__)
#define DEFINE_PRIVATE_ACCESSOR_CONST_FUNCTION(Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(Class, Class, ReturnType, MemberName, const, __VA_ARGS__)
#define DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION(Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION_EX(Class, Class, ReturnType, MemberName, __VA_ARGS__)

#define DEFINE_PRIVATE_ACCESSOR_FUNCTION_TAG(ClassTag, Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(ClassTag, Class, ReturnType, MemberName, , __VA_ARGS__)
#define DEFINE_PRIVATE_ACCESSOR_CONST_FUNCTION_TAG(ClassTag, Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(ClassTag, Class, ReturnType, MemberName, const, __VA_ARGS__)

#define PRIVATE_ACCESS_OBJ(ClassTag, MemberName, Obj) (Obj.*PRIVATE_ACCESS(ClassTag, MemberName))
#define PRIVATE_ACCESS_PTR(ClassTag, MemberName, Ptr) (Ptr->*PRIVATE_ACCESS(ClassTag, MemberName))
#define PRIVATE_ACCESS_STATIC(ClassTag, MemberName) (*PRIVATE_ACCESS(ClassTag, MemberName))

/****************************** Use Cases ******************************/

#if 0

// For any class with private members:

#include <cstdint>
#include <cstdio>
#include <map>

class FTestClass
{
	static const FTestClass* Instance;
	int32_t Value = 42;
	void Increment() { Value++; }
	static bool Register(const FTestClass* Ptr) { Instance = Ptr; return true; }

	void Register(std::map<const FTestClass*, int32_t>& Dictionary) const { Dictionary[this] = Value; }

public:
	void Print() const { printf("Instance %p Value %d\n", Instance, Value); }
};

inline const FTestClass* FTestClass::Instance = nullptr;

// Define accessors as follows:

DEFINE_PRIVATE_ACCESSOR_VARIABLE(FTestClass, int32_t, Value);
DEFINE_PRIVATE_ACCESSOR_STATIC_VARIABLE(FTestClass, const FTestClass*, Instance);
DEFINE_PRIVATE_ACCESSOR_FUNCTION(FTestClass, void, Increment);
DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION(FTestClass, bool, Register, const FTestClass*);

using FTestClassIndexMap = std::map<const FTestClass*, int32_t>; // Alias complex type names so we can pass to the macros
DEFINE_PRIVATE_ACCESSOR_CONST_FUNCTION_TAG(FTestClassToMap, FTestClass, void, Register, FTestClassIndexMap&); // Use different tags for overloaded functions

// Use it anywhere!

inline void PrivateAccessorTest()
{
	// Where our target data is stored
	FTestClass Obj;
	const FTestClass* Ptr = &Obj;

	// Get member variable
	const int32_t* Value = &PRIVATE_ACCESS_PTR(FTestClass, Value, Ptr);

	// Invoke member function
	PRIVATE_ACCESS_OBJ(FTestClass, Increment, Obj)();

	// Invoke static function
	bool bSuccess = PRIVATE_ACCESS_STATIC(FTestClass, Register)(Ptr);

	// Set static variable
	PRIVATE_ACCESS_STATIC(FTestClass, Instance) = (const FTestClass*)0xdeadbeef;

	// Invoke overloaded function
	FTestClassIndexMap TestClassIndexMap;
	PRIVATE_ACCESS_PTR(FTestClassToMap, Register, Ptr)(TestClassIndexMap);

	Obj.Print();
	printf("LocalValue %d Success %d MapValue %d\n", *Value, bSuccess, TestClassIndexMap[Ptr]);
	fflush(stdout);
}

#endif
