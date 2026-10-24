// c:\Users\ivand\Documents\Coding\Project\U-Monitor\tests\ExamMonitor.Server.Tests\Program.cs
using System.Reflection;

namespace ExamMonitor.Server.Tests;

public static class ServerTestAssert
{
    public static void True(bool condition, string message = "Expected condition to be true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message = "Expected condition to be false.")
    {
        if (condition) throw new InvalidOperationException(message);
    }

    public static void NotNull(object? value, string message = "Expected non-null reference.")
    {
        if (value is null) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Assert.Equal failed. Expected: <{expected}>, Actual: <{actual}>.");
        }
    }
}

public static class Program
{
    public static async Task<int> Main()
    {
        var suite = new SessionAndViewModelTests();
        MethodInfo[] methods = typeof(SessionAndViewModelTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        int passed = 0;
        int failed = 0;

        foreach (MethodInfo method in methods)
        {
            try
            {
                object? result = method.Invoke(suite, null);
                if (result is Task task)
                {
                    await task;
                }
                Console.WriteLine($"[PASS] ExamMonitor.Server.Tests.{method.Name}");
                passed++;
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
                Console.Error.WriteLine($"[FAIL] ExamMonitor.Server.Tests.{method.Name}: {inner.Message}");
                failed++;
            }
        }

        Console.WriteLine($"Test Summary (ExamMonitor.Server.Tests): {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}
