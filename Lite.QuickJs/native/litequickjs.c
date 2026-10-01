/* Lite's stable C ABI over the pinned QuickJS source. JSValue never crosses P/Invoke. */
#include "quickjs.h"
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#define LITE_API __declspec(dllexport)
#else
#define LITE_API __attribute__((visibility("default")))
#endif

typedef struct LiteValue {
    JSContext *context;
    JSValue value;
} LiteValue;

typedef LiteValue *(*LiteHostCallback)(JSContext *, int32_t, LiteValue *, int32_t);
typedef char *(*LiteNormalizeCallback)(JSContext *, const char *, const char *);
typedef char *(*LiteModuleSourceCallback)(JSContext *, const char *, size_t *);
typedef void (*LiteReleaseCallback)(char *);
typedef void (*LiteRejectionCallback)(JSContext *, LiteValue *, LiteValue *, int);
typedef int (*LiteInterruptCallback)(void);

typedef struct LiteRuntimeState {
    LiteHostCallback callback;
    LiteNormalizeCallback normalize;
    LiteModuleSourceCallback module_source;
    LiteReleaseCallback release;
    LiteRejectionCallback rejection;
    LiteInterruptCallback interrupt;
    JSClassID host_class_id;
} LiteRuntimeState;

static JSClassID lite_host_class_id;

static LiteValue *lite_wrap(JSContext *context, JSValue value) {
    LiteValue *result = malloc(sizeof(*result));
    if (!result) {
        JS_FreeValue(context, value);
        return NULL;
    }
    result->context = context;
    result->value = value;
    return result;
}

LITE_API JSRuntime *lite_runtime_new(void) {
    JSRuntime *runtime = JS_NewRuntime();
    if (!runtime) return NULL;
    LiteRuntimeState *state = calloc(1, sizeof(*state));
    if (!state) { JS_FreeRuntime(runtime); return NULL; }
    if (!lite_host_class_id) JS_NewClassID(&lite_host_class_id);
    JSClassDef host_class = { .class_name = "LiteHostObject" };
    if (JS_NewClass(runtime, lite_host_class_id, &host_class) < 0) {
        free(state);
        JS_FreeRuntime(runtime);
        return NULL;
    }
    state->host_class_id = lite_host_class_id;
    JS_SetRuntimeOpaque(runtime, state);
    JS_SetCanBlock(runtime, 0);
    return runtime;
}

LITE_API void lite_runtime_free(JSRuntime *runtime) {
    if (!runtime) return;
    LiteRuntimeState *state = JS_GetRuntimeOpaque(runtime);
    JS_FreeRuntime(runtime);
    free(state);
}

LITE_API JSContext *lite_context_new(JSRuntime *runtime) {
    JSContext *context = JS_NewContext(runtime);
    if (!context) return NULL;
    LiteRuntimeState *state = JS_GetRuntimeOpaque(runtime);
    /* Host objects must behave like ordinary JS objects: chain the host class
     * prototype to Object.prototype so inherited methods (hasOwnProperty,
     * toString, valueOf, ...) work on DOM wrappers, as other engines provide.
     * Without this the prototype is NULL and, e.g., WPT's testharness.js
     * crashes calling NodeList.hasOwnProperty(index) in its results renderer. */
    JSValue probe = JS_NewObject(context);
    JS_SetClassProto(context, state->host_class_id, JS_GetPrototype(context, probe));
    JS_FreeValue(context, probe);
    return context;
}
LITE_API void lite_context_free(JSContext *context) { if (context) JS_FreeContext(context); }
LITE_API void lite_runtime_set_memory_limit(JSRuntime *runtime, size_t bytes) { JS_SetMemoryLimit(runtime, bytes); }
LITE_API void lite_runtime_set_stack_limit(JSRuntime *runtime, size_t bytes) { JS_SetMaxStackSize(runtime, bytes); }
LITE_API void lite_runtime_update_stack_top(JSRuntime *runtime) { if (runtime) JS_UpdateStackTop(runtime); }
LITE_API void lite_runtime_set_can_block(JSRuntime *runtime, int can_block) { JS_SetCanBlock(runtime, can_block != 0); }

LITE_API LiteValue *lite_value_dup(LiteValue *value) {
    return value ? lite_wrap(value->context, JS_DupValue(value->context, value->value)) : NULL;
}

LITE_API LiteValue *lite_value_to_context(JSContext *context, LiteValue *value) {
    if (JS_GetRuntime(context) != JS_GetRuntime(value->context)) return NULL;
    return lite_wrap(context, JS_DupValue(context, value->value));
}

LITE_API void lite_value_free(LiteValue *value) {
    if (!value) return;
    JS_FreeValue(value->context, value->value);
    free(value);
}

LITE_API int lite_value_kind(const LiteValue *value) {
    if (!value) return -1;
    if (JS_IsUndefined(value->value)) return 0;
    if (JS_IsNull(value->value)) return 1;
    if (JS_IsBool(value->value)) return 2;
    if (JS_IsNumber(value->value)) return 3;
    if (JS_IsString(value->value)) return 4;
    if (JS_IsFunction(value->context, value->value)) return 6;
    if (JS_IsObject(value->value)) return 5;
    return 7;
}

LITE_API int lite_value_same(LiteValue *left, LiteValue *right) {
    if (!left || !right || JS_GetRuntime(left->context) != JS_GetRuntime(right->context)) return 0;
    return JS_SameValue(left->context, left->value, right->value);
}

LITE_API uintptr_t lite_value_identity(LiteValue *value) {
    return value && JS_IsObject(value->value) ? (uintptr_t)JS_VALUE_GET_PTR(value->value) : 0;
}

LITE_API LiteValue *lite_new_undefined(JSContext *context) { return lite_wrap(context, JS_UNDEFINED); }
LITE_API LiteValue *lite_new_null(JSContext *context) { return lite_wrap(context, JS_NULL); }
LITE_API LiteValue *lite_new_bool(JSContext *context, int value) { return lite_wrap(context, JS_NewBool(context, value)); }
LITE_API LiteValue *lite_new_number(JSContext *context, double value) { return lite_wrap(context, JS_NewFloat64(context, value)); }
LITE_API LiteValue *lite_new_string(JSContext *context, const char *value, size_t length) {
    return lite_wrap(context, JS_NewStringLen(context, value, length));
}
LITE_API LiteValue *lite_new_object(JSContext *context) { return lite_wrap(context, JS_NewObject(context)); }
LITE_API LiteValue *lite_global(JSContext *context) { return lite_wrap(context, JS_GetGlobalObject(context)); }

LITE_API LiteValue *lite_new_host_object(JSContext *context, int32_t id) {
    LiteRuntimeState *state = JS_GetRuntimeOpaque(JS_GetRuntime(context));
    JSValue object = JS_NewObjectClass(context, state->host_class_id);
    if (JS_IsException(object)) return NULL;
    JS_SetOpaque(object, (void *)(intptr_t)id);
    return lite_wrap(context, object);
}

LITE_API int32_t lite_host_object_id(LiteValue *value) {
    if (!value || !JS_IsObject(value->value)) return 0;
    LiteRuntimeState *state = JS_GetRuntimeOpaque(JS_GetRuntime(value->context));
    return (int32_t)(intptr_t)JS_GetOpaque(value->value, state->host_class_id);
}

LITE_API int lite_value_is_error(LiteValue *value) {
    return value ? JS_IsError(value->context, value->value) : 0;
}

LITE_API void lite_gc(JSRuntime *runtime) { JS_RunGC(runtime); }

LITE_API void lite_detach_array_buffer(LiteValue *value) {
    JS_DetachArrayBuffer(value->context, value->value);
}

LITE_API void lite_set_html_dda(LiteValue *value) {
    JS_SetIsHTMLDDA(value->context, value->value);
}

LITE_API char *lite_to_string(LiteValue *value, size_t *result_length) {
    if (!value) return NULL;
    size_t length = 0;
    const char *text = JS_ToCStringLen(value->context, &length, value->value);
    if (!text) return NULL;
    char *copy = malloc(length + 1);
    if (copy) { memcpy(copy, text, length); copy[length] = 0; *result_length = length; }
    JS_FreeCString(value->context, text);
    return copy;
}

LITE_API void lite_string_free(char *value) { free(value); }
LITE_API int lite_to_number(LiteValue *value, double *result) { return JS_ToFloat64(value->context, result, value->value); }
LITE_API int lite_to_bool(LiteValue *value) { return JS_ToBool(value->context, value->value); }

LITE_API LiteValue *lite_eval(JSContext *context, const char *source, size_t length,
                              const char *filename, int flags) {
    JSValue result;
    if ((flags & JS_EVAL_TYPE_MASK) == JS_EVAL_TYPE_MODULE) {
        JSValue compiled = JS_Eval(context, source, length, filename,
            flags | JS_EVAL_FLAG_COMPILE_ONLY);
        if (JS_IsException(compiled)) return NULL;
        JSModuleDef *module = JS_VALUE_GET_PTR(compiled);
        JSValue meta = JS_GetImportMeta(context, module);
        if (JS_IsException(meta)) { JS_FreeValue(context, compiled); return NULL; }
        JS_SetPropertyStr(context, meta, "url", JS_NewString(context, filename));
        JS_FreeValue(context, meta);
        result = JS_EvalFunction(context, compiled);
    } else {
        result = JS_Eval(context, source, length, filename, flags);
    }
    if (JS_IsException(result)) return NULL;
    return lite_wrap(context, result);
}

LITE_API int lite_compile(JSContext *context, const char *source, size_t length,
                          const char *filename, int flags) {
    JSValue compiled = JS_Eval(context, source, length, filename, flags | JS_EVAL_FLAG_COMPILE_ONLY);
    if (JS_IsException(compiled)) return -1;
    JS_FreeValue(context, compiled);
    return 0;
}

LITE_API LiteValue *lite_compile_value(JSContext *context, const char *source, size_t length,
                                       const char *filename, int flags) {
    JSValue compiled = JS_Eval(context, source, length, filename, flags | JS_EVAL_FLAG_COMPILE_ONLY);
    if (JS_IsException(compiled)) return NULL;
    return lite_wrap(context, compiled);
}

LITE_API int lite_resolve_module(LiteValue *compiled) {
    return JS_ResolveModule(compiled->context, compiled->value);
}

LITE_API LiteValue *lite_eval_compiled(LiteValue *compiled) {
    JSValue result = JS_EvalFunction(compiled->context,
        JS_DupValue(compiled->context, compiled->value));
    if (JS_IsException(result)) return NULL;
    return lite_wrap(compiled->context, result);
}

LITE_API LiteValue *lite_get_exception(JSContext *context) { return lite_wrap(context, JS_GetException(context)); }

LITE_API LiteValue *lite_get_property(LiteValue *target, const char *name) {
    JSValue result = JS_GetPropertyStr(target->context, target->value, name);
    if (JS_IsException(result)) return NULL;
    return lite_wrap(target->context, result);
}

LITE_API int lite_set_property(LiteValue *target, const char *name, LiteValue *value) {
    return JS_SetPropertyStr(target->context, target->value, name,
        JS_DupValue(target->context, value->value));
}

LITE_API int lite_define_accessor(LiteValue *target, const char *name,
                                  LiteValue *getter, LiteValue *setter) {
    JSContext *context = target->context;
    JSAtom atom = JS_NewAtom(context, name);
    if (atom == JS_ATOM_NULL) return -1;
    JSValue get_value = getter ? JS_DupValue(context, getter->value) : JS_UNDEFINED;
    JSValue set_value = setter ? JS_DupValue(context, setter->value) : JS_UNDEFINED;
    int result = JS_DefinePropertyGetSet(context, target->value, atom,
        get_value, set_value, JS_PROP_CONFIGURABLE | JS_PROP_ENUMERABLE);
    JS_FreeAtom(context, atom);
    return result;
}

LITE_API LiteValue *lite_call(LiteValue *function, LiteValue *this_value,
                              LiteValue *arguments, int count) {
    JSValue *values = count ? malloc((size_t)count * sizeof(*values)) : NULL;
    if (count && !values) return NULL;
    for (int i = 0; i < count; i++) values[i] = (&arguments[i])->value;
    JSValue result = JS_Call(function->context, function->value,
        this_value ? this_value->value : JS_UNDEFINED, count, values);
    free(values);
    if (JS_IsException(result)) return NULL;
    return lite_wrap(function->context, result);
}

LITE_API LiteValue *lite_call_handles(LiteValue *function, LiteValue *this_value,
                                      LiteValue **arguments, int count) {
    JSValue *values = count ? malloc((size_t)count * sizeof(*values)) : NULL;
    if (count && !values) return NULL;
    for (int i = 0; i < count; i++) values[i] = arguments[i]->value;
    JSValue result = JS_Call(function->context, function->value,
        this_value ? this_value->value : JS_UNDEFINED, count, values);
    free(values);
    if (JS_IsException(result)) return NULL;
    return lite_wrap(function->context, result);
}

LITE_API LiteValue *lite_construct(LiteValue *function, LiteValue **arguments, int count) {
    JSValue *values = count ? malloc((size_t)count * sizeof(*values)) : NULL;
    if (count && !values) return NULL;
    for (int i = 0; i < count; i++) values[i] = arguments[i]->value;
    JSValue result = JS_CallConstructor(function->context, function->value, count, values);
    free(values);
    if (JS_IsException(result)) return NULL;
    return lite_wrap(function->context, result);
}

LITE_API LiteValue *lite_new_promise(JSContext *context,
                                      LiteValue **resolve, LiteValue **reject) {
    JSValue functions[2] = { JS_UNDEFINED, JS_UNDEFINED };
    JSValue promise = JS_NewPromiseCapability(context, functions);
    if (JS_IsException(promise)) return NULL;
    LiteValue *wrapped_promise = lite_wrap(context, promise);
    LiteValue *wrapped_resolve = lite_wrap(context, functions[0]);
    LiteValue *wrapped_reject = lite_wrap(context, functions[1]);
    if (!wrapped_promise || !wrapped_resolve || !wrapped_reject) {
        lite_value_free(wrapped_promise);
        lite_value_free(wrapped_resolve);
        lite_value_free(wrapped_reject);
        return NULL;
    }
    *resolve = wrapped_resolve;
    *reject = wrapped_reject;
    return wrapped_promise;
}

LITE_API int lite_execute_job(JSRuntime *runtime, JSContext **exception_context) {
    return JS_ExecutePendingJob(runtime, exception_context);
}

LITE_API int lite_jobs_pending(JSRuntime *runtime) { return JS_IsJobPending(runtime); }

static void lite_track_rejection(JSContext *context, JSValueConst promise,
                                 JSValueConst reason, JS_BOOL is_handled, void *opaque) {
    LiteRuntimeState *state = opaque;
    if (!state->rejection) return;
    LiteValue borrowed_promise = { context, promise };
    LiteValue borrowed_reason = { context, reason };
    state->rejection(context, &borrowed_promise, &borrowed_reason, is_handled);
}

LITE_API void lite_set_rejection_callback(JSRuntime *runtime, LiteRejectionCallback callback) {
    LiteRuntimeState *state = JS_GetRuntimeOpaque(runtime);
    state->rejection = callback;
    JS_SetHostPromiseRejectionTracker(runtime, lite_track_rejection, state);
}

static int lite_interrupt(JSRuntime *runtime, void *opaque) {
    LiteRuntimeState *state = opaque;
    return state->interrupt ? state->interrupt() : 0;
}

LITE_API void lite_set_interrupt_callback(JSRuntime *runtime, LiteInterruptCallback callback) {
    LiteRuntimeState *state = JS_GetRuntimeOpaque(runtime);
    state->interrupt = callback;
    JS_SetInterruptHandler(runtime, lite_interrupt, state);
}

static char *lite_normalize_module(JSContext *context, const char *base_name,
                                   const char *specifier, void *opaque) {
    LiteRuntimeState *state = opaque;
    char *name = state->normalize(context, base_name, specifier);
    if (!name) { JS_ThrowReferenceError(context, "module resolution failed: %s", specifier); return NULL; }
    char *copy = js_strdup(context, name);
    state->release(name);
    return copy;
}

static JSModuleDef *lite_load_module(JSContext *context, const char *module_name, void *opaque) {
    LiteRuntimeState *state = opaque;
    size_t length = 0;
    char *source = state->module_source(context, module_name, &length);
    if (!source) { JS_ThrowReferenceError(context, "module source unavailable: %s", module_name); return NULL; }
    JSValue compiled = JS_Eval(context, source, length, module_name,
        JS_EVAL_TYPE_MODULE | JS_EVAL_FLAG_COMPILE_ONLY);
    state->release(source);
    if (JS_IsException(compiled)) return NULL;
    JSModuleDef *module = JS_VALUE_GET_PTR(compiled);
    JSValue meta = JS_GetImportMeta(context, module);
    if (!JS_IsException(meta)) {
        JS_SetPropertyStr(context, meta, "url", JS_NewString(context, module_name));
        JS_FreeValue(context, meta);
    }
    JS_FreeValue(context, compiled);
    return module;
}

LITE_API void lite_set_module_callbacks(JSRuntime *runtime, LiteNormalizeCallback normalize,
                                        LiteModuleSourceCallback source, LiteReleaseCallback release) {
    LiteRuntimeState *state = JS_GetRuntimeOpaque(runtime);
    state->normalize = normalize;
    state->module_source = source;
    state->release = release;
    JS_SetModuleLoaderFunc(runtime, lite_normalize_module, lite_load_module, state);
}

static JSValue lite_host_call(JSContext *context, JSValueConst this_value,
                              int argc, JSValueConst *argv, int magic, JSValue *data) {
    (void)this_value;
    (void)magic;
    LiteRuntimeState *state = JS_GetRuntimeOpaque(JS_GetRuntime(context));
    if (!state || !state->callback) return JS_ThrowInternalError(context, "host callback unavailable");
    int32_t id = 0;
    JS_ToInt32(context, &id, data[0]);
    LiteValue *borrowed = argc ? malloc((size_t)argc * sizeof(*borrowed)) : NULL;
    if (argc && !borrowed) return JS_ThrowOutOfMemory(context);
    for (int i = 0; i < argc; i++) {
        borrowed[i].context = context;
        borrowed[i].value = argv[i];
    }
    LiteValue *owned = state->callback(context, id, borrowed, argc);
    free(borrowed);
    if (!owned) return JS_EXCEPTION;
    JSValue result = owned->value;
    free(owned);
    return result;
}

LITE_API void lite_set_host_callback(JSRuntime *runtime, LiteHostCallback callback) {
    LiteRuntimeState *state = JS_GetRuntimeOpaque(runtime);
    state->callback = callback;
}

LITE_API LiteValue *lite_argument_at(LiteValue *arguments, int index) {
    return &arguments[index];
}

LITE_API LiteValue *lite_new_host_function(JSContext *context, int32_t id,
                                            const char *name, int arity) {
    JSValue data = JS_NewInt32(context, id);
    JSValue result = JS_NewCFunctionData(context, lite_host_call, arity, 0, 1, &data);
    JS_FreeValue(context, data);
    if (JS_IsException(result)) return NULL;
    if (name) JS_SetPropertyStr(context, result, "name", JS_NewString(context, name));
    return lite_wrap(context, result);
}

LITE_API void lite_throw_error(JSContext *context, const char *message) {
    JS_ThrowInternalError(context, "%s", message ? message : "host error");
}

LITE_API void lite_throw_value(JSContext *context, LiteValue *value) {
    JS_Throw(context, JS_DupValue(context, value->value));
}
