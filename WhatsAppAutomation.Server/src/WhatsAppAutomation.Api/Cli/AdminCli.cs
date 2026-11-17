using System.Text;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Members;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Api.Cli;

/// <summary>
/// Server-side admin commands. There is no sign-up screen: the platform admin is created here, once, by
/// whoever runs the server. Everyone else is created on the screens (tenants and their admins by the platform
/// admin, members by their tenant admin).
/// <code>
/// dotnet run -- migrate
/// dotnet run -- create-platform-admin --username samir --display-name "Samir"
/// dotnet run -- new-secret
/// </code>
/// The password is asked for twice without echo, or read from one line of standard input when it is redirected.
/// </summary>
internal static class AdminCli
{
    private static readonly string[] Commands = ["migrate", "create-platform-admin"];

    public static bool IsCliCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0], StringComparer.Ordinal);

    /// <summary>
    /// "new-secret" prints 32 random bytes in base64, for Gateway:Secret and GatewayDev:AuthStateKey.
    /// It needs no database, so it runs before the app is built. Returns null for other commands.
    /// </summary>
    public static int? TryRunWithoutHost(string[] args, TextWriter output)
    {
        if (args is not ["new-secret"])
        {
            return null;
        }

        output.WriteLine(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        return 0;
    }

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TextReader input)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        try
        {
            return args[0] switch
            {
                "migrate" => await MigrateAsync(provider, output),
                "create-platform-admin" => await CreatePlatformAdminAsync(provider, args[1..], output, input),
                _ => Usage(output),
            };
        }
        catch (ArgumentException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            Usage(output);
            return 2;
        }
    }

    private static async Task<int> MigrateAsync(IServiceProvider provider, TextWriter output)
    {
        var db = provider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await output.WriteLineAsync("Database is up to date.");
        return 0;
    }

    private static async Task<int> CreatePlatformAdminAsync(IServiceProvider provider, string[] args, TextWriter output, TextReader input)
    {
        var options = ParseOptions(args);
        var userName = Required(options, "--username").Trim();
        var displayName = (options.GetValueOrDefault("--display-name") ?? userName).Trim();
        if (userName.Length > 50 || !userName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new ArgumentException("User name may only use letters, digits, '.', '_' and '-' (at most 50).");
        }

        if (displayName.Length is 0 or > 100)
        {
            throw new ArgumentException($"Display name needs 1 to {100} characters.");
        }

        var identity = provider.GetRequiredService<IIdentityService>();
        if (await identity.UserNameExistsAsync(userName, CancellationToken.None))
        {
            await output.WriteLineAsync($"Error: a user named '{userName}' already exists.");
            return 1;
        }

        var password = ReadPassword(input, output);
        if (PasswordRules.Problem(password, userName, isAdmin: true) is { } problem)
        {
            await output.WriteLineAsync($"Error: {problem}");
            return 1;
        }

        // Chosen by the person who will use it, so there is no need to change it at the first sign-in.
        var result = await identity.CreateUserAsync(
            new NewUser(userName, displayName, password, UserKind.PlatformAdmin, null, [], MustChangePassword: false),
            CancellationToken.None);
        if (!result.IsSuccess)
        {
            await output.WriteLineAsync($"Error: {result.Error!.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Created platform admin '{userName}'. Sign in on the website to add tenants.");
        return 0;
    }

    private static string ReadPassword(TextReader input, TextWriter output)
    {
        if (Console.IsInputRedirected || !ReferenceEquals(input, Console.In))
        {
            return input.ReadLine() ?? throw new ArgumentException("No password on standard input.");
        }

        var first = ReadHidden(output, "Password: ");
        var second = ReadHidden(output, "Repeat password: ");
        return first == second ? first : throw new ArgumentException("The passwords do not match.");
    }

    private static string ReadHidden(TextWriter output, string prompt)
    {
        output.Write(prompt);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value' but found '{args[i]}'.");
            }

            options[args[i]] = args[i + 1];
        }

        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");

    private static int Usage(TextWriter output)
    {
        output.WriteLine("Commands:");
        output.WriteLine("  migrate");
        output.WriteLine("  create-platform-admin --username <name> [--display-name <name>]   (asks for the password)");
        output.WriteLine("  new-secret      prints a random 32-byte secret (base64)");
        return 2;
    }
}
