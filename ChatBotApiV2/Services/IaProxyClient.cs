using ClbModChatbot;
using System.Text.Json;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ChatBotApiV2.Models;

namespace ChatBotApiV2.Services
{
    public class IaProxyClient : IIaProxyClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IaProxyClient> _logger;

        public IaProxyClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<IaProxyClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async IAsyncEnumerable<string> StreamFromPythonAsync(ClsModOllamaChatMessages payload, [EnumeratorCancellation] CancellationToken ct)
        {
            var baseUrl = _configuration["PythonApi:BaseUrl"] ?? "http://localhost:8000";
            var url = baseUrl.TrimEnd('/') + "/chat-stream";

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonPayload = JsonSerializer.Serialize(payload, options);

            _logger.LogDebug("IaProxyClient POST {Url} payload: {Payload}", url, jsonPayload);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try
                {
                    body = await response.Content.ReadAsStringAsync(ct);
                }
                catch { }

                _logger.LogError("Python API returned {Status} when calling {Url}. Body: {Body}", (int)response.StatusCode, url, body);
                throw new HttpRequestException($"Python API returned status {(int)response.StatusCode}: {body}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var eventBuffer = new StringBuilder();
            var hasData = false;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                _logger.LogTrace("IaProxyClient raw line: {Line}", line);

                // Normalize line endings
                if (string.IsNullOrWhiteSpace(line))
                {
                    // event delimiter: process accumulated data lines
                    if (hasData)
                    {
                        var payloadJson = eventBuffer.ToString();
                        eventBuffer.Clear();
                        hasData = false;

                        // The accumulated payload may contain multiple 'data:' parts concatenated
                        // Trim and yield
                        payloadJson = payloadJson.Trim();
                        if (!string.IsNullOrEmpty(payloadJson))
                        {
                            _logger.LogDebug("IaProxyClient emitting aggregated event payload: {Payload}", payloadJson);
                            yield return payloadJson;
                        }
                    }
                    continue;
                }

                // SSE data line
                if (line.StartsWith("data:"))
                {
                    var part = line.Substring("data:".Length).Trim();
                    // Log part being appended
                    _logger.LogTrace("IaProxyClient appending data part: {Part}", part);
                    // If multiple 'data:' lines belong to the same event, SSE semantics
                    // require joining them with '\n'. Insert newline between parts when
                    // reassembling to preserve original content boundaries.
                    if (eventBuffer.Length > 0)
                        eventBuffer.Append('\n');
                    eventBuffer.Append(part);
                    hasData = true;
                    continue;
                }

                // Some servers may send the JSON directly without 'data:' prefix
                var trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                {
                    // treat as complete JSON event
                    _logger.LogDebug("IaProxyClient emitting direct JSON event: {Payload}", trimmed);
                    yield return trimmed;
                    continue;
                }

                // ignore other SSE fields (id:, event:, retry:)
                _logger.LogDebug("IaProxyClient ignoring SSE line: {Line}", line);
            }

            // Flush any remaining buffer if stream ended without trailing blank line
            if (hasData && eventBuffer.Length > 0)
            {
                var payloadJson = eventBuffer.ToString().Trim();
                if (!string.IsNullOrEmpty(payloadJson))
                {
                    _logger.LogDebug("IaProxyClient flushing remaining payload: {Payload}", payloadJson);
                    yield return payloadJson;
                }
            }
        }

        public async Task<OrchestrateResponseDto> CallOrchestrateAsync(OrchestrateRequestDto payload, CancellationToken ct)
        {
            var baseUrl = _configuration["PythonApi:BaseUrl"] ?? "http://localhost:8000";
            var url = baseUrl.TrimEnd('/') + "/orchestrate";

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonPayload = JsonSerializer.Serialize(payload, options);
            _logger.LogDebug("IaProxyClient POST {Url} orchestrate payload: {Payload}", url, jsonPayload);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try
                {
                    body = await response.Content.ReadAsStringAsync(ct);
                }
                catch { }

                _logger.LogError("Python orchestrate returned {Status} when calling {Url}. Body: {Body}", (int)response.StatusCode, url, body);
                throw new HttpRequestException($"Python orchestrate returned status {(int)response.StatusCode}: {body}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (contentType != null && contentType.Contains("text/event-stream"))
            {
                // Orchestrator devolvió SSE (llm_chat o web_search). No deserializar JSON.
                _logger.LogInformation("Orchestrate returned SSE (Content-Type: {ContentType}), signaling streaming fallback", contentType);
                return new OrchestrateResponseDto { action = "sse_stream" };
            }

            var contentStream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<OrchestrateResponseDto>(contentStream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, ct);

            if (result == null)
            {
                throw new InvalidOperationException("Orchestrate response was null or could not be deserialized.");
            }

            _logger.LogInformation("Orchestrate action {Action} (debug: {DebugAction} - {Reason})", result.action, result.debug?.action, result.debug?.reason);
            return result;
        }

        public async IAsyncEnumerable<string> StreamOrchestrateAsync(OrchestrateRequestDto payload, [EnumeratorCancellation] CancellationToken ct)
        {
            var baseUrl = _configuration["PythonApi:BaseUrl"] ?? "http://localhost:8000";
            var url = baseUrl.TrimEnd('/') + "/agent-stream";

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonPayload = JsonSerializer.Serialize(payload, options);
            _logger.LogDebug("IaProxyClient POST {Url} orchestrate payload: {Payload}", url, jsonPayload);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try { body = await response.Content.ReadAsStringAsync(ct); } catch { }
                _logger.LogError("Python orchestrate returned {Status} when calling {Url}. Body: {Body}", (int)response.StatusCode, url, body);
                throw new HttpRequestException($"Python orchestrate returned status {(int)response.StatusCode}: {body}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();

            if (contentType != null && contentType.Contains("text/event-stream"))
            {
                // SSE stream
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var eventBuffer = new StringBuilder();
                var hasData = false;

                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        if (hasData)
                        {
                            var payloadJson = eventBuffer.ToString().Trim();
                            eventBuffer.Clear(); hasData = false;
                            if (!string.IsNullOrEmpty(payloadJson)) yield return payloadJson;
                        }
                        continue;
                    }

                    if (line.StartsWith("data:"))
                    {
                        var part = line.Substring("data:".Length).Trim();
                        if (eventBuffer.Length > 0) eventBuffer.Append('\n');
                        eventBuffer.Append(part);
                        hasData = true;
                        continue;
                    }

                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                    {
                        yield return trimmed;
                        continue;
                    }
                }

                if (hasData && eventBuffer.Length > 0)
                {
                    var payloadJson = eventBuffer.ToString().Trim();
                    if (!string.IsNullOrEmpty(payloadJson)) yield return payloadJson;
                }
            }
            else
            {
                // JSON single response
                var raw = await response.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(raw)) yield return raw;
            }
        }
    }
}
