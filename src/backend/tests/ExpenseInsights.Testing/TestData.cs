namespace ExpenseInsights.Testing;

/// <summary>The generated fake exports in <c>test-data/</c>, copied next to the test assembly.</summary>
public static class TestData
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "test-data", name);

    public static byte[] Read(string name) => File.ReadAllBytes(PathOf(name));
}
