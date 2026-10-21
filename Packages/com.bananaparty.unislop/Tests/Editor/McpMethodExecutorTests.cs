using System;
using NUnit.Framework;

namespace UniSlop.MCP.Tests
{
    public class McpMethodExecutorTests
    {
        const string TargetType = "UniSlop.MCP.Tests.McpMethodExecutorTests+Target";

        // Reflection target for the executor; also exercises nested-type resolution ('+').
        static class Target
        {
            public static int LastValue;

            public static int Add(int a, int b) => a + b;

            public static string Greet(string name) => "Hello " + name;

            public static void SetValue(int value) => LastValue = value;

            public static bool Toggle(bool flag) => !flag;

            public static string Describe(DayOfWeek day) => day.ToString();

            public static string NoArgs() => "no-args";

            static string Hidden() => "hidden";

            public static void Throw() => throw new InvalidOperationException("intentional failure");
        }

        [Test]
        public void Execute_ReturnsConvertedResult()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".Add", new[] { "2", "3" });
            Assert.That(json, Does.Contain("\"status\":\"success\""));
            Assert.That(json, Does.Contain("\"returnValue\":\"5\""));
            Assert.That(json, Does.Contain("\"returnType\":\"System.Int32\""));
        }

        [Test]
        public void Execute_StringArgument_PassedVerbatim()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".Greet", new[] { "World" });
            Assert.That(json, Does.Contain("\"returnValue\":\"Hello World\""));
        }

        [Test]
        public void Execute_VoidMethod_ReportsNullReturnValue()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".SetValue", new[] { "7" });
            Assert.That(json, Does.Contain("\"status\":\"success\""));
            Assert.That(json, Does.Contain("\"returnType\":\"void\""));
            Assert.That(json, Does.Contain("\"returnValue\":null"));
            Assert.AreEqual(7, Target.LastValue);
        }

        [Test]
        public void Execute_NoArguments_NullArgsAccepted()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".NoArgs", null);
            Assert.That(json, Does.Contain("\"returnValue\":\"no-args\""));
        }

        [Test]
        public void Execute_BoolAndEnumArguments_AreConverted()
        {
            string toggled = McpMethodExecutor.Execute(TargetType + ".Toggle", new[] { "true" });
            Assert.That(toggled, Does.Contain("\"returnValue\":\"False\""));

            string described = McpMethodExecutor.Execute(TargetType + ".Describe", new[] { "Friday" });
            Assert.That(described, Does.Contain("\"returnValue\":\"Friday\""));
        }

        [Test]
        public void Execute_NonPublicMethod_IsCallable()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".Hidden", null);
            Assert.That(json, Does.Contain("\"returnValue\":\"hidden\""));
        }

        [Test]
        public void Execute_ThrowingMethod_ReturnsErrorWithExceptionDetails()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".Throw", null);
            Assert.That(json, Does.Contain("\"status\":\"error\""));
            Assert.That(json, Does.Contain("InvalidOperationException"));
            Assert.That(json, Does.Contain("intentional failure"));
        }

        [Test]
        public void Execute_UnknownType_ReturnsError()
        {
            string json = McpMethodExecutor.Execute("No.Such.Type.Method", null);
            Assert.That(json, Does.Contain("\"status\":\"error\""));
            Assert.That(json, Does.Contain("not found"));
        }

        [Test]
        public void Execute_UnknownMethod_ReturnsError()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".DoesNotExist", null);
            Assert.That(json, Does.Contain("\"status\":\"error\""));
            Assert.That(json, Does.Contain("No static method"));
        }

        [Test]
        public void Execute_WrongArgumentCount_ReturnsError()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".Add", new[] { "1" });
            Assert.That(json, Does.Contain("\"status\":\"error\""));
            Assert.That(json, Does.Contain("no overload taking 1 parameter(s)"));
        }

        [Test]
        public void Execute_UnconvertibleArgument_ReturnsError()
        {
            string json = McpMethodExecutor.Execute(TargetType + ".Add", new[] { "two", "3" });
            Assert.That(json, Does.Contain("\"status\":\"error\""));
            Assert.That(json, Does.Contain("cannot convert argument 1"));
        }

        [Test]
        public void Execute_MissingMethodPath_ReturnsError()
        {
            string json = McpMethodExecutor.Execute(null, null);
            Assert.That(json, Does.Contain("\"status\":\"error\""));
            Assert.That(json, Does.Contain("Missing 'method'"));
        }

        [Test]
        public void Handle_ExecuteMethodCommand_RoutesToExecutor()
        {
            string json = McpUnityBridge.Handle(new McpRequest
            {
                command = "execute_method",
                method = TargetType + ".Add",
                args = new[] { "20", "22" }
            });
            Assert.That(json, Does.Contain("\"status\":\"success\""));
            Assert.That(json, Does.Contain("\"returnValue\":\"42\""));
        }
    }
}
