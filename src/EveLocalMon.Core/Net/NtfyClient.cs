using System.Net.Http.Headers;
using System.Text;
using EveLocalMon.Core.Alerts;

namespace EveLocalMon.Core.Net;

/// <summary>
/// Posts an alert to an ntfy topic. Fails soft: a dead network or a typo'd server must never take
/// down the channels the user is actually looking at.
/// </summary>
public sealed class NtfyClient(HttpClient httpClient) : INtfyClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<bool> SendAsync(AlertMessage message, NtfyOptions options, CancellationToken cancellationToken = default)
    {
        // One retry: pushes are best-effort, and a second failure means the problem isn't transient.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(Timeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(options))
                {
                    Content = new StringContent(message.Body, Encoding.UTF8),
                };

                request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
                request.Headers.TryAddWithoutValidation("Title", message.Title);
                request.Headers.TryAddWithoutValidation("Priority", PriorityHeader(message.Priority));
                request.Headers.TryAddWithoutValidation("Tags", TagsHeader(message.Kind));

                using HttpResponseMessage response = await httpClient.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }
        }

        return false;
    }

    private static Uri BuildUri(NtfyOptions options) => new($"{options.Server.TrimEnd('/')}/{options.Topic}");

    private static string PriorityHeader(AlertPriority priority) => priority switch
    {
        AlertPriority.Urgent => "urgent",
        AlertPriority.High => "high",
        _ => "low",
    };

    private static string TagsHeader(AlertKind kind) => kind switch
    {
        AlertKind.ThreatIncreased => "rotating_light",
        AlertKind.Unreadable or AlertKind.MemberCountWhileUnreadable => "warning",
        _ => "white_check_mark",
    };
}
