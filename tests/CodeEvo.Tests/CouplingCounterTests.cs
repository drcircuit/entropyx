using CodeEvo.Core;
using Xunit;

namespace CodeEvo.Tests;

public class CouplingCounterTests
{
    // ── CSharp ────────────────────────────────────────────────────────────────

    [Fact]
    public void Count_CSharp_CountsUsingDirectives()
    {
        var lines = new[]
        {
            "using System;",
            "using System.Collections.Generic;",
            "using CodeEvo.Core;",
            "",
            "namespace Foo { }"
        };
        Assert.Equal(3, CouplingCounter.Count(lines, "CSharp"));
    }

    [Fact]
    public void Count_CSharp_IgnoresUsingStatements()
    {
        var lines = new[]
        {
            "using var conn = OpenConnection();",
            "using (var stream = new FileStream()) { }",
            "using FileStream fs = File.OpenRead(\"x\");"
        };
        Assert.Equal(0, CouplingCounter.Count(lines, "CSharp"));
    }

    // ── Java ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Count_Java_CountsImports()
    {
        var lines = new[]
        {
            "import java.util.List;",
            "import java.io.IOException;",
            "public class Foo { }"
        };
        Assert.Equal(2, CouplingCounter.Count(lines, "Java"));
    }

    // ── Python ───────────────────────────────────────────────────────────────

    [Fact]
    public void Count_Python_CountsImportsAndFromImports()
    {
        var lines = new[]
        {
            "import os",
            "import sys",
            "from pathlib import Path",
            "x = 1"
        };
        Assert.Equal(3, CouplingCounter.Count(lines, "Python"));
    }

    // ── TypeScript / JavaScript ───────────────────────────────────────────────

    [Fact]
    public void Count_TypeScript_CountsImportStatements()
    {
        var lines = new[]
        {
            "import { Component } from '@angular/core';",
            "import React from 'react';",
            "const x = 1;"
        };
        Assert.Equal(2, CouplingCounter.Count(lines, "TypeScript"));
    }

    [Fact]
    public void Count_JavaScript_CountsRequireCalls()
    {
        var lines = new[]
        {
            "const fs = require('fs');",
            "const path = require('path');",
            "module.exports = {};"
        };
        Assert.Equal(2, CouplingCounter.Count(lines, "JavaScript"));
    }

    // ── C / C++ ───────────────────────────────────────────────────────────────

    [Fact]
    public void Count_C_CountsIncludes()
    {
        var lines = new[]
        {
            "#include <stdio.h>",
            "#include \"myheader.h\"",
            "int main() { return 0; }"
        };
        Assert.Equal(2, CouplingCounter.Count(lines, "C"));
    }

    [Fact]
    public void Count_Cpp_CountsIncludes()
    {
        var lines = new[]
        {
            "#include <iostream>",
            "#include <vector>",
            "int main() { }"
        };
        Assert.Equal(2, CouplingCounter.Count(lines, "Cpp"));
    }

    // ── Rust ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Count_Rust_CountsUseAndExternCrate()
    {
        var lines = new[]
        {
            "use std::collections::HashMap;",
            "extern crate serde;",
            "fn main() { }"
        };
        Assert.Equal(2, CouplingCounter.Count(lines, "Rust"));
    }

    // ── General ───────────────────────────────────────────────────────────────

    [Fact]
    public void Count_EmptyLines_ReturnsZero()
    {
        var lines = new[] { "", "  ", "\t" };
        Assert.Equal(0, CouplingCounter.Count(lines, "CSharp"));
    }

    [Fact]
    public void Count_UnknownLanguage_ReturnsZero()
    {
        var lines = new[] { "import something", "using System;" };
        Assert.Equal(0, CouplingCounter.Count(lines, ""));
    }
}
