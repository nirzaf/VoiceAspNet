using Twilio.AspNet.Core.MinimalApi;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment()) 
    builder.Services.AddHostedService<WebhookService.TunnelService>();

var app = builder.Build();
var configuration = app.Configuration;

app.MapPost("/voice/incoming", () =>
{
    var voiceResponse = new VoiceResponse();
    var dial = new Dial();
    dial.Client("my-identity");
    voiceResponse.Append(dial);
    
    return Results.Extensions.TwiML(voiceResponse);
});

app.MapPost("/voice/outgoing", async (HttpRequest httpRequest) =>
{
    var form = await httpRequest.ReadFormAsync();
    var toPhoneNumber = form["To"];
    var twilioPhoneNumber = configuration["Twilio:PhoneNumber"] ?? throw new Exception("Twilio:PhoneNumber configuration is required");
    
    var voiceResponse = new VoiceResponse();
    voiceResponse.Dial(number: toPhoneNumber, callerId: twilioPhoneNumber);
    
    return Results.Extensions.TwiML(voiceResponse);
});

app.Run();
