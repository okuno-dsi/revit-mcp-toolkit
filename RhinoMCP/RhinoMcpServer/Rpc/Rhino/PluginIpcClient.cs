using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace RhinoMcpServer.Rpc.Rhino
{
    public static class PluginIpcClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static async Task<string> PostAsync(string json)
        {
            var res = await _http.PostAsync("http://127.0.0.1:5201/rpc", new StringContent(json, Encoding.UTF8, "application/json"));
            return await res.Content.ReadAsStringAsync();
        }
    }
}
