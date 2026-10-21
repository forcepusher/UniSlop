using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace UniSlop.MCP
{
    // Executes a static C# method by fully qualified name via reflection. Runs on the Unity main
    // thread (dispatched through McpMainThread) so the target method can safely use editor APIs.
    public static class McpMethodExecutor
    {
        public static string Execute(string methodPath, string[] args)
        {
            if (string.IsNullOrEmpty(methodPath))
                return McpUnityBridge.Error("Missing 'method' parameter (expected 'Namespace.Class.Method')");

            int lastDot = methodPath.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == methodPath.Length - 1)
                return McpUnityBridge.Error($"Invalid method path '{methodPath}' (expected 'Namespace.Class.Method')");

            string typeName = methodPath.Substring(0, lastDot);
            string methodName = methodPath.Substring(lastDot + 1);
            if (args == null)
                args = new string[0];

            Type type = FindType(typeName);
            if (type == null)
                return McpUnityBridge.Error($"Type '{typeName}' not found in loaded assemblies (use '+' for nested types, e.g. 'Namespace.Outer+Nested')");

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var candidates = new List<MethodInfo>();
            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                if (method.Name == methodName && !method.IsGenericMethodDefinition)
                    candidates.Add(method);
            }

            if (candidates.Count == 0)
                return McpUnityBridge.Error($"No static method '{methodName}' found on type '{typeName}'");

            MethodInfo match = null;
            object[] converted = null;
            string conversionError = null;
            foreach (MethodInfo candidate in candidates)
            {
                if (candidate.GetParameters().Length != args.Length)
                    continue;
                if (TryConvertArguments(candidate, args, out converted, out conversionError))
                {
                    match = candidate;
                    break;
                }
            }

            if (match == null)
            {
                if (conversionError != null)
                    return McpUnityBridge.Error($"Cannot call '{methodPath}': {conversionError}");
                return McpUnityBridge.Error($"Static method '{methodPath}' has no overload taking {args.Length} parameter(s)");
            }

            object result;
            try
            {
                result = match.Invoke(null, converted);
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                string data = "{\"exception\":" + McpUnityBridge.JsonStr(inner.GetType().FullName)
                    + ",\"stackTrace\":" + McpUnityBridge.JsonStr(inner.StackTrace) + "}";
                return McpUnityBridge.Error($"Method '{methodPath}' threw {inner.GetType().Name}: {inner.Message}", data);
            }

            string returnValue = match.ReturnType == typeof(void)
                ? null
                : (result == null ? "null" : Convert.ToString(result, CultureInfo.InvariantCulture));

            string dataJson = "{\"returnType\":" + McpUnityBridge.JsonStr(FriendlyTypeName(match.ReturnType))
                + ",\"returnValue\":" + McpUnityBridge.JsonStr(returnValue) + "}";
            return McpUnityBridge.Success($"Executed '{methodPath}'", dataJson);
        }

        static Type FindType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(typeName, false); }
                catch { continue; }
                if (type != null)
                    return type;
            }
            return null;
        }

        static bool TryConvertArguments(MethodInfo method, string[] args, out object[] converted, out string error)
        {
            ParameterInfo[] parameters = method.GetParameters();
            converted = new object[parameters.Length];
            error = null;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (!TryConvertArgument(args[i], parameters[i].ParameterType, out converted[i]))
                {
                    error = $"cannot convert argument {i + 1} ('{args[i]}') to {FriendlyTypeName(parameters[i].ParameterType)}";
                    return false;
                }
            }
            return true;
        }

        static bool TryConvertArgument(string value, Type targetType, out object result)
        {
            result = null;
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (underlying == typeof(string))
                    result = value;
                else if (underlying.IsEnum)
                    result = Enum.Parse(underlying, value, true);
                else if (underlying == typeof(bool))
                    result = bool.Parse(value.Trim().ToLowerInvariant());
                else
                    result = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string FriendlyTypeName(Type type)
        {
            return type == typeof(void) ? "void" : type.FullName;
        }
    }
}
