using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace RhinoMcpServer.Rpc.RevitProxy
{
    public static class RevitMcpClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static async Task<string> PostAsync(string baseUrl, string json)
        {
            var res = await _http.PostAsync(baseUrl.TrimEnd('/') + "/rpc", new StringContent(json, Encoding.UTF8, "application/json"));
            return await res.Content.ReadAsStringAsync();
        }

        public static async Task<string> PostRpcAndWaitAsync(string baseUrl, string json, int timeoutMs = 60000)
        {
            var enqueueUrl = baseUrl.TrimEnd('/') + "/enqueue?force=1";
            var getUrl = baseUrl.TrimEnd('/') + "/get_result";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var post = await _http.PostAsync(enqueueUrl, content);
            // ignore body; poll for result
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var gr = await _http.GetAsync(getUrl);
                if ((int)gr.StatusCode == 202 || (int)gr.StatusCode == 204)
                {
                    await Task.Delay(200);
                    continue;
                }
                return await gr.Content.ReadAsStringAsync();
            }
            throw new TimeoutException("Timed out waiting for Revit MCP result");
        }
    }
}
