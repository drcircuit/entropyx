using CodeEvo.Core;
using Xunit;

namespace CodeEvo.Tests;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("Foo.java", "Java")]
    [InlineData("Bar.cs", "CSharp")]
    [InlineData("main.c", "C")]
    [InlineData("header.h", "C")]
    [InlineData("app.cpp", "Cpp")]
    [InlineData("app.cc", "Cpp")]
    [InlineData("app.cxx", "Cpp")]
    [InlineData("app.hpp", "Cpp")]
    [InlineData("index.ts", "TypeScript")]
    [InlineData("component.tsx", "TypeScript")]
    [InlineData("index.js", "JavaScript")]
    [InlineData("component.jsx", "JavaScript")]
    [InlineData("module.mjs", "JavaScript")]
    [InlineData("module.cjs", "JavaScript")]
    [InlineData("lib.rs", "Rust")]
    [InlineData("script.py", "Python")]
    [InlineData("readme.md", "")]
    [InlineData("noextension", "")]
    public void Detect_ReturnsCorrectLanguage(string filePath, string expectedLanguage)
    {
        var result = LanguageDetector.Detect(filePath);
        Assert.Equal(expectedLanguage, result);
    }
}
