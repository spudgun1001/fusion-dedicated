namespace FusionDedicated.Commands;

/// <summary>
/// Reads commands from standard input. Pterodactyl pipes its console straight to
/// the process, so this is the panel's command surface.
/// </summary>
public static class StdinCommands
{
    public static void Start(CommandProcessor processor, Action<string> write, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await Console.In.ReadLineAsync(token);
                }
                catch
                {
                    return;
                }

                // Null means stdin closed, which happens with no console attached.
                if (line is null)
                {
                    return;
                }

                try
                {
                    string reply = processor.Execute(line);

                    if (!string.IsNullOrEmpty(reply))
                    {
                        write(reply);
                    }
                }
                catch (Exception ex)
                {
                    write($"Command failed: {ex.Message}");
                }
            }
        }, token);
    }
}
