using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Exceptionless.Extensions.DevOps.Clients
{
    public class DevOpsClient : IDevOpsClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _pat;

        public DevOpsClient(IHttpClientFactory httpClientFactory, string pat)
        {
            _httpClientFactory = httpClientFactory;
            _pat = pat;
        }

        public async Task<WorkItemStatus?> GetWorkItemStatus(string workItemId)
        {
            var httpClient = _httpClientFactory.CreateClient("devops-odata");
            var encodedPat = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));

            var request = new HttpRequestMessage(HttpMethod.Get, $"WorkItems?$filter=WorkItemId eq {workItemId}&$select=State");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedPat);

            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();

            using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
            {
                var root = doc.RootElement;
                var valueArray = root.GetProperty("value");

                if (valueArray.GetArrayLength() > 0)
                {
                    var firstItem = valueArray[0];
                    var workItemStatusStr = firstItem.GetProperty("State").GetString();

                    if (!string.IsNullOrEmpty(workItemStatusStr))
                    {
                        return workItemStatusStr.ToWorkItemStatus();
                    }
                }
            }

            return null;
        }

        public async Task<IResult> UpdateWorkItemStatus(string workItemId, WorkItemStatus newStatus)
        {
            var httpClient = _httpClientFactory.CreateClient("devops-services");
            var encodedPat = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));

            var request = new HttpRequestMessage(HttpMethod.Patch, $"WorkItems/{workItemId}?api-version=7.1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedPat);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new[] {
                    new { op = "add", path = "/fields/System.State", value = newStatus.ToFriendlyString() }
                }),
                Encoding.UTF8, "application/json-patch+json");

            var response = await httpClient.SendAsync(request);

            return Results.StatusCode((int)response.StatusCode);
        }
    }
}
