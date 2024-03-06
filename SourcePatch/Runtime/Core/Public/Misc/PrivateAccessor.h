#pragma once

/**
 * A tiny library for accessing private members from non-friend classes.
 *
 * Ref: http://bloglitb.blogspot.com/2010/07/access-to-private-members-thats-easy.html
 */

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

#define DEFINE_PRIVATE_ACCESSOR_EX(Class, MemberType, MemberName, ClassTag) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef MemberType (Class::*Type); }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define DEFINE_PRIVATE_ACCESSOR_STATIC_EX(Class, MemberType, MemberName, ClassTag) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef MemberType* Type; }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(Class, ReturnType, MemberName, ClassTag, ...) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef ReturnType(Class::*Type)(__VA_ARGS__); }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION_EX(Class, ReturnType, MemberName, ClassTag, ...) \
	struct F##ClassTag##MemberName##PrivateAccessor { typedef ReturnType(*Type)(__VA_ARGS__); }; \
	template struct TRob<F##ClassTag##MemberName##PrivateAccessor, &Class::MemberName>

#define PRIVATE_ACCESS(ClassTag, MemberName) TResult<F##ClassTag##MemberName##PrivateAccessor>::Ptr

// Syntactic sugars

#define DEFINE_PRIVATE_ACCESSOR(Class, MemberType, MemberName) DEFINE_PRIVATE_ACCESSOR_EX(Class, MemberType, MemberName, Class)
#define DEFINE_PRIVATE_ACCESSOR_STATIC(Class, MemberType, MemberName) DEFINE_PRIVATE_ACCESSOR_STATIC_EX(Class, MemberType, MemberName, Class)
#define DEFINE_PRIVATE_ACCESSOR_FUNCTION(Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_FUNCTION_EX(Class, ReturnType, MemberName, Class, __VA_ARGS__)
#define DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION(Class, ReturnType, MemberName, ...) DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION_EX(Class, ReturnType, MemberName, Class, __VA_ARGS__)

#define PRIVATE_ACCESS_OBJ(ClassTag, MemberName, Obj) (Obj.*PRIVATE_ACCESS(ClassTag, MemberName))
#define PRIVATE_ACCESS_PTR(ClassTag, MemberName, Ptr) (Ptr->*PRIVATE_ACCESS(ClassTag, MemberName))
#define PRIVATE_ACCESS_STATIC(ClassTag, MemberName) (*PRIVATE_ACCESS(ClassTag, MemberName))

#if 0

/****************************** Use Cases ******************************/

// For any class with private members:

#include <cstdint>
#include <cstdio>

class FTestClass
{
	static const FTestClass* Instance;
	int32_t Value = 42;
	void Increment() { Value++; }
	static bool Register(const FTestClass* Ptr) { Instance = Ptr; return true; }

public:
	void Print() const { printf("Instance %p Value %d\n", Instance, Value); fflush(stdout); }
};

inline const FTestClass* FTestClass::Instance = nullptr;

// Define accessors as follows:

DEFINE_PRIVATE_ACCESSOR(FTestClass, int32_t, Value);
DEFINE_PRIVATE_ACCESSOR_STATIC(FTestClass, const FTestClass*, Instance);
DEFINE_PRIVATE_ACCESSOR_FUNCTION(FTestClass, void, Increment);
DEFINE_PRIVATE_ACCESSOR_STATIC_FUNCTION(FTestClass, bool, Register, const FTestClass*);

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
	PRIVATE_ACCESS_STATIC(FTestClass, Instance) = nullptr;

	Obj.Print();
}

#endif
