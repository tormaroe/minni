using Shouldly;

namespace MinniStore.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Parse_WithEmptyArgs_ReturnsDefaults()
    {
        var result = CommandLineParser.Parse([]);
        
        result.Port.ShouldBe(25000);
        result.DbPath.ShouldBe("minni.db");
        result.ShowHelp.ShouldBeFalse();
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Parse_WithValidPort_SetsPort()
    {
        var result = CommandLineParser.Parse(["--port", "12345"]);
        
        result.Port.ShouldBe(12345);
        result.DbPath.ShouldBe("minni.db");
        result.ShowHelp.ShouldBeFalse();
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Parse_WithMissingPortValue_ReturnsError()
    {
        var result = CommandLineParser.Parse(["--port"]);
        
        result.ErrorMessage.ShouldBe("Missing value for --port");
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    public void Parse_WithInvalidPortValue_ReturnsError(string invalidPort)
    {
        var result = CommandLineParser.Parse(["--port", invalidPort]);
        
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("Invalid port number");
    }

    [Fact]
    public void Parse_WithValidDb_SetsDbPath()
    {
        var result = CommandLineParser.Parse(["--db", "custom.db"]);
        
        result.Port.ShouldBe(25000);
        result.DbPath.ShouldBe("custom.db");
        result.ShowHelp.ShouldBeFalse();
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Parse_WithMissingDbValue_ReturnsError()
    {
        var result = CommandLineParser.Parse(["--db"]);
        
        result.ErrorMessage.ShouldBe("Missing value for --db");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithEmptyDbValue_ReturnsError(string emptyDb)
    {
        var result = CommandLineParser.Parse(["--db", emptyDb]);
        
        result.ErrorMessage.ShouldBe("Database file path cannot be empty.");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_WithHelpFlag_SetsShowHelp(string flag)
    {
        var result = CommandLineParser.Parse([flag]);
        
        result.ShowHelp.ShouldBeTrue();
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Parse_WithUnknownArgument_ReturnsError()
    {
        var result = CommandLineParser.Parse(["--unknown"]);
        
        result.ErrorMessage.ShouldBe("Unknown argument: --unknown");
    }

    [Fact]
    public void PrintHelp_RunsWithoutExceptions()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            CommandLineParser.PrintHelp();
            var output = sw.ToString();
            output.ShouldContain("Usage:");
            output.ShouldContain("--port");
            output.ShouldContain("--db");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void ProgramMain_WithHelp_ReturnsZero()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var exitCode = Program.Main(["--help"]);
            exitCode.ShouldBe(0);
            sw.ToString().ShouldContain("Usage:");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void ProgramMain_WithMissingPort_ReturnsOne()
    {
        var originalError = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var exitCode = Program.Main(["--port"]);
            exitCode.ShouldBe(1);
            sw.ToString().ShouldContain("Error: Missing value for --port");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void ProgramMain_WithInvalidContentRoot_ReturnsOne()
    {
        var originalError = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var exitCode = Program.Main(["--contentRoot=invalid\0path"]);
            exitCode.ShouldBe(1);
            sw.ToString().ShouldContain("Application startup failed");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
