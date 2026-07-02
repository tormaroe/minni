namespace MinniStore;

public class CommandLineOptions
{
    public int Port { get; set; } = 25000;
    public string DbPath { get; set; } = "minni.db";
    public bool ShowHelp { get; set; }
    public string? ErrorMessage { get; set; }
}
