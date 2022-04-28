using System.Text.Json.Nodes;
using CliWrap;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace WebhookService;

public class TunnelService : BackgroundService
{
    private readonly IServer server;
    private readonly IHostApplicationLifetime hostApplicationLifetime;
    private readonly IConfiguration configuration;
    private readonly ILogger<TunnelService> logger;

    public TunnelService(
        IServer server,
        IHostApplicationLifetime hostApplicationLifetime,
        IConfiguration configuration,
        ILogger<TunnelService> logger
    )
    {
        this.server = server;
        this.hostApplicationLifetime = hostApplicationLifetime;
        this.configuration = configuration;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStarted();

        var urls = server.Features.Get<IServerAddressesFeature>()!.Addresses;
        // Use https:// if you authenticated ngrok, otherwise, you can only use http://
        var localUrl = urls.Single(u => u.StartsWith("https://"));

        logger.LogInformation("Starting ngrok tunnel for {LocalUrl}", localUrl);
        var ngrokTask = StartNgrokTunnel(localUrl, stoppingToken);

        var publicUrl = await GetNgrokPublicUrl(localUrl);
        logger.LogInformation("Public ngrok URL: {NgrokPublicUrl}", publicUrl);

        await ConfigureTwilioWebhook(publicUrl);

        await ngrokTask;

        logger.LogInformation("Ngrok tunnel stopped");
    }

    private Task WaitForApplicationStarted()
    {
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hostApplicationLifetime.ApplicationStarted.Register(() => completionSource.TrySetResult());
        return completionSource.Task;
    }

    private CommandTask<CommandResult> StartNgrokTunnel(string localUrl, CancellationToken stoppingToken)
    {
        var ngrokTask = Cli.Wrap("ngrok")
            .WithArguments(args => args
                .Add("http")
                .Add(localUrl)
                .Add("--log")
                .Add("stdout"))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger.LogDebug(s)))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger.LogError(s)))
            .ExecuteAsync(stoppingToken);
        return ngrokTask;
    }

    private async Task<string> GetNgrokPublicUrl(string localUrl)
    {
        using var httpClient = new HttpClient();
        for (var ngrokRetryCount = 0; ngrokRetryCount < 10; ngrokRetryCount++)
        {
            logger.LogDebug("Get ngrok tunnels attempt: {RetryCount}", ngrokRetryCount + 1);

            try
            {
                var json = await httpClient.GetFromJsonAsync<JsonNode>("http://127.0.0.1:4040/api/tunnels");
                var publicUrl = json["tunnels"].AsArray()
                    .Where(e => e["config"]?["addr"].GetValue<string>().Equals(localUrl) == true)
                    .Where(e => e["public_url"].GetValue<string>().StartsWith("https://"))
                    .Select(e => e["public_url"].GetValue<string>())
                    .SingleOrDefault();
                    
                if (!string.IsNullOrEmpty(publicUrl)) return publicUrl;
            }
            catch
            {
                // ignored
            }

            await Task.Delay(200);
        }

        throw new Exception("Ngrok dashboard did not start in 10 tries");
    }

    private async Task ConfigureTwilioWebhook(string publicUrl)
    {
        var twilioAccountSid = configuration["Twilio:AccountSid"] ?? throw new Exception("Twilio:AccountSid configuration is required");
        var twilioApiKeySid = configuration["Twilio:ApiKeySid"] ?? throw new Exception("Twilio:ApiKeySid configuration is required");
        var twilioApiKeySecret = configuration["Twilio:ApiKeySecret"] ?? throw new Exception("Twilio:ApiKeySecret configuration is required");
        var twilioApplicationSid = configuration["Twilio:ApplicationSid"] ?? throw new Exception("Twilio:ApplicationSid configuration is required");
        var twilioPhoneNumber = configuration["Twilio:PhoneNumber"] ?? throw new Exception("Twilio:PhoneNumber configuration is required");

        var twilioClient = new TwilioRestClient(twilioApiKeySid, twilioApiKeySecret, twilioAccountSid);

        var phoneNumber = (await IncomingPhoneNumberResource.ReadAsync(
            phoneNumber: new PhoneNumber(twilioPhoneNumber),
            limit: 1,
            client: twilioClient
        )).Single();

        phoneNumber = await IncomingPhoneNumberResource.UpdateAsync(
            phoneNumber.Sid,
            voiceUrl: new Uri($"{publicUrl}/voice/incoming"),
            voiceMethod: Twilio.Http.HttpMethod.Post,
            client: twilioClient
        );
        logger.LogInformation(
            "Twilio Phone Number {TwilioPhoneNumber} Voice URL updated to {TwilioVoiceUrl}",
            phoneNumber.PhoneNumber,
            phoneNumber.VoiceUrl
        );

        var application = await ApplicationResource.UpdateAsync(
            twilioApplicationSid,
            voiceUrl: new Uri($"{publicUrl}/voice/outgoing"),
            voiceMethod: Twilio.Http.HttpMethod.Post,
            client: twilioClient
        );
        logger.LogInformation(
            "Twilio Application '{ApplicationFriendlyName}' Voice URL updated to {ApplicationVoiceUrl}",
            application.FriendlyName,
            application.VoiceUrl
        );
    }
}