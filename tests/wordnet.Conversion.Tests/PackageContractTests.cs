using System;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace WordNet.Tests;

public sealed class PackageContractTests
{
    [Fact]
    public void LoadedLibraryUsesTheRequestedFrameworkAsset()
    {
#if NET472
        const string defaultTarget = ".NETFramework,Version=v4.7.2";
#elif NET8_0
        const string defaultTarget = ".NETCoreApp,Version=v8.0";
#else
        const string defaultTarget = ".NETCoreApp,Version=v10.0";
#endif
        string expected = Environment.GetEnvironmentVariable("WORDNET_EXPECTED_TFM") ?? defaultTarget;
        Assert.Equal(expected, typeof(WordConverter).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }
}
