using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Results;
using Identity.Contracts.Users;
using Rebus.Bus;
using Rebus.Handlers;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// Stands in for Identity on the real broker (CONVENTIONS.md "Testing": "other services are not run —
/// assert on contracts published to the real broker"). Handles the exact wire types Identity's
/// own <c>SignUpHandler</c>/<c>LoginHandler</c> do — <see cref="SignUp"/>/<see cref="Login"/> in,
/// <see cref="SignUpReply"/>/<see cref="LoginReply"/> or <see cref="ReplyFault"/> out — so
/// AuthGrpcService is exercised against the real request/reply bridge end to end, never a
/// re-implementation of it. The two <see cref="Error"/>s below restate Identity's own
/// <c>UserErrors.EmailAlreadyRegistered</c>/<c>InvalidCredentials</c> by code/kind rather than
/// referencing Identity.Application (a different service's Application ring, out of bounds for
/// this repo) — matching them is exactly what proves the Gateway maps the real wire contract, not
/// a fixture-only stand-in for it.
/// </summary>
public sealed class FakeIdentityResponder(IBus bus) : IHandleMessages<SignUp>, IHandleMessages<Login>
{
    public const string DuplicateEmail = "duplicate@gateway-tests.local";
    public const string NeverRepliesEmail = "never-replies@gateway-tests.local";
    public const string InvalidCredentialsPassword = "wrong-password";

    public static readonly Error EmailAlreadyRegistered = new(
        "identity.email_already_registered", "A user with this email is already registered.", ErrorKind.Conflict);

    public static readonly Error InvalidCredentials = new(
        "identity.invalid_credentials", "Email or password is incorrect.", ErrorKind.Unauthenticated);

    public async Task Handle(SignUp message)
    {
        if (message.Email == NeverRepliesEmail)
        {
            // Deliberately does nothing — stands in for a handler that never gets to reply, so
            // the Gateway's own reply-timeout path (messaging.reply_timeout -> UNAVAILABLE) is
            // exercised for real rather than assumed.
            return;
        }

        if (message.Email == DuplicateEmail)
        {
            await bus.Reply(ReplyFault.From(EmailAlreadyRegistered));
            return;
        }

        // The token encodes both fields, not just the email, so a test can catch the swap a
        // three-positional-string constructor invites (Email/DisplayName, or worse
        // Email/AccessToken on the Gateway side) rather than merely proving *some* string came
        // back non-empty.
        await bus.Reply(new SignUpReply(Guid.NewGuid(), FormatAccessToken(message.Email, message.DisplayName), DateTimeOffset.UtcNow.AddHours(1)));
    }

    /// <summary>Deterministic from both inputs, so a test can assert the exact string rather than "non-empty".</summary>
    public static string FormatAccessToken(string email, string secondField) => $"token|{email}|{secondField}";

    public async Task Handle(Login message)
    {
        if (message.Email == NeverRepliesEmail)
        {
            return;
        }

        if (message.Password == InvalidCredentialsPassword)
        {
            await bus.Reply(ReplyFault.From(InvalidCredentials));
            return;
        }

        await bus.Reply(new LoginReply(Guid.NewGuid(), FormatAccessToken(message.Email, message.Password), DateTimeOffset.UtcNow.AddHours(1)));
    }
}
