using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Users;
using WhatsAppAutomation.Application.Features.Users.Commands;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Api.Cli;

/// <summary>
/// Server-side admin commands. There is no sign-up screen: users are created here.
/// <code>
/// dotnet run -- migrate
/// dotnet run -- create-user --username dhaval --display-name "Dhaval" --role Owner
/// dotnet run -- new-secret
/// </code>
/// The password is asked for twice without echo, or read from one line of standard input when it is redirected.
/// </summary>
internal static class AdminCli
{
    private static readonly string[] Commands = ["migrate", "create-user"];

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
                "create-user" => await CreateUserAsync(provider, args[1..], output, input),
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

    private static async Task<int> CreateUserAsync(IServiceProvider provider, string[] args, TextWriter output, TextReader input)
    {
        var options = ParseOptions(args);
        var userName = Required(options, "--username");
        var displayName = options.GetValueOrDefault("--display-name") ?? userName;
        var roleName = Required(options, "--role");

        // Roles live in the database (Owner and Sender come from the migration, the Owner can add more).
        var roles = await provider.GetRequiredService<IRoleService>().ListAsync(CancellationToken.None);
        var role = roles.FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown role '{roleName}'. Existing roles: {string.Join(", ", roles.Select(r => r.Name))}. Run 'migrate' first if this list is empty.");

        var password = ReadPassword(input, output);

        // The CLI sends the same command the API uses, so the same validation and rules apply.
        var result = await provider.GetRequiredService<ISender>()
            .Send(new CreateUserCommand(userName, displayName, password, [role.Id]));

        if (!result.IsSuccess)
        {
            await output.WriteLineAsync($"Error: {result.Error!.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Created user '{userName}' with role {role.Name}.");
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
        output.WriteLine("  create-user --username <name> [--display-name <name>] --role <role name, e.g. Owner>");
        output.WriteLine("  new-secret      prints a random 32-byte secret (base64)");
        return 2;
    }
}
