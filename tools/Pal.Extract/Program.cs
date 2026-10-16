if (args.Length != 0 && (args.Length != 1 || args[0] != "--help"))
{
    Console.Error.WriteLine($"Unsupported arguments: {string.Join(" ", args)}");
    Console.Error.WriteLine("Use --help for usage.");
    return 2;
}

Console.WriteLine("""
    pal-extract - Offline resource conversion tool for godot-pal.

    Usage: pal-extract [--help]
      --help  Show this help message.

    Resource extraction is not implemented yet (M0 scaffold only).
    """);
return 0;
