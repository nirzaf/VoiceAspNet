using Twilio.Jwt.AccessToken;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var configuration = app.Configuration;

app.MapPost("/api/token", () =>
{
    string twilioAccountSid = configuration["Twilio:AccountSid"] ?? throw new Exception("Twilio:AccountSid configuration is required");
    string twilioApiKeySid = configuration["Twilio:ApiKeySid"] ?? throw new Exception("Twilio:ApiKeySid configuration is required");
    string twilioApiKeySecret = configuration["Twilio:ApiKeySecret"] ?? throw new Exception("Twilio:ApiKeySecret configuration is required");
    string twilioApplicationSid = configuration["Twilio:ApplicationSid"] ?? throw new Exception("Twilio:ApplicationSid configuration is required");
    string identity = "my-identity";

    var grants = new HashSet<IGrant>
    {
        new VoiceGrant
        {
            OutgoingApplicationSid = twilioApplicationSid,
            IncomingAllow = true
        }
    };

    var token = new Token(
        twilioAccountSid,
        twilioApiKeySid,
        twilioApiKeySecret,
        identity,
        grants: grants
    );

    return token.ToJwt();
});

app.Run();
