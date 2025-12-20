using System;
using System.Net;
using System.Text;

namespace RhinoMcpPlugin.Core
{
    public static class HttpJsonRpcClient
    {
        public static string PostJson(string url, string json)
        {
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Encoding = Encoding.UTF8;
                try
                {
                    return wc.UploadString(url, "POST", json);
                }
                catch (WebException ex)
                {
                    using (var sr = new System.IO.StreamReader(ex.Response.GetResponseStream()))
                    {
                        var body = sr.ReadToEnd();
                        throw new Exception($"HTTP error: {ex.Message} body={body}");
                    }
                }
            }
        }
    }
}
